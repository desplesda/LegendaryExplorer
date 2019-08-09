﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Gammtek.Conduit.Extensions.IO;
using ME3Explorer.SharedUI;
using ME3Explorer.Unreal;
using StreamHelpers;
using static ME3Explorer.Unreal.UnrealFlags;

namespace ME3Explorer.Packages
{
    public enum PackageChange
    {
        ExportData,
        ExportHeader,
        Import,
        Names,
        ExportAdd,
        ImportAdd,
    }

    [DebuggerDisplay("PackageUpdate | {change} on index {index}")]
    public struct PackageUpdate
    {
        /// <summary>
        /// Details on what piece of data has changed
        /// </summary>
        public PackageChange change;
        /// <summary>
        /// 0-based index of what item has changed in this package -1 = import 0, 0 = export 0
        /// </summary>
        public int index;
    }

    public sealed class MEPackage : UnrealPackageFile, IMEPackage, IDisposable
    {
        public MEGame Game { get; } //can only be ME1, ME2, or ME3. UDK is a seperate class

        public bool CanReconstruct =>
            Game == MEGame.ME3 ||
            Game == MEGame.ME2 ||
            Game == MEGame.ME1 && !exports.Any(x => x.IsTexture() && EmbeddedTextureViewer.GetTexture2DMipInfos(x,null).Any(mip => mip.storageType == StorageTypes.pccLZO || 
                                                                                                                                            mip.storageType == StorageTypes.pccZlib));

        public int FullHeaderSize { get; private set; }
        public EPackageFlags Flags { get; private set; }

        public override int NameCount { get; protected set; }
        public int NameOffset { get; private set; }
        public override int ExportCount { get; protected set; }
        public int ExportOffset { get; private set; }
        public override int ImportCount { get; protected set; }
        public int ImportOffset { get; private set; }
        public int DependencyTableOffset { get; private set; }
        public Guid PackageGuid { get; set; }

        public byte[] getHeader()
        {
            var ms = new MemoryStream();
            WriteHeader(ms);
            return ms.ToArray();
        }

        public bool IsCompressed
        {
            get => Flags.HasFlag(EPackageFlags.Compressed);
            private set
            {
                if (value)
                {
                    //Toolkit should never set this flag as we do not support compressing files.
                    Flags |= EPackageFlags.Compressed;
                }
                else // else set to false
                {
                    Flags &= ~EPackageFlags.Compressed;
                    PackageCompressionType = CompressionType.None;
                }
            }
        }

        public enum CompressionType
        {
            None = 0,
            Zlib,
            LZO
        }

        public CompressionType PackageCompressionType { get; private set; }

        #region HeaderMisc
        private int Gen0ExportCount;
        private int Gen0NameCount;
        private int Gen0NetworkedObjectCount;
        private int ImportExportGuidsOffset;
        private int ImportGuidsCount;
        private int ExportGuidsCount;
        private int ThumbnailTableOffset;
        private int engineVersion;
        private int cookedContentVersion;
        private uint packageSource;
        private int unknown1;
        private int unknown2;
        private int unknown3;
        private int unknown4;
        private int unknown5;
        private int unknown6;
        private int unknown7;
        private int unknown8;
        private int unknown9;
        #endregion

        static bool isInitialized;
        public static Func<string, MEPackage> Initialize()
        {
            if (isInitialized)
            {
                throw new Exception(nameof(MEPackage) + " can only be initialized once");
            }

            isInitialized = true;
            return f => new MEPackage(f);
        }

        private MEPackage(string filePath)
        {
            ME3ExpMemoryAnalyzer.MemoryAnalyzer.AddTrackedMemoryItem($"MEPackage {Path.GetFileName(filePath)}", new WeakReference(this));

            FilePath = Path.GetFullPath(filePath);

            using (var fs = File.OpenRead(filePath))
            {
                #region Header

                uint magic = fs.ReadUInt32();
                if (magic != packageTag)
                {
                    throw new FormatException("Not an Unreal package!");
                }
                ushort unrealVersion = fs.ReadUInt16();
                ushort licenseeVersion = fs.ReadUInt16();
                switch (unrealVersion)
                {
                    case 491 when licenseeVersion == 1008:
                        Game = MEGame.ME1;
                        break;
                    case 512 when licenseeVersion == 130:
                        Game = MEGame.ME2;
                        break;
                    case 684 when licenseeVersion == 194:
                        Game = MEGame.ME3;
                        break;
                    default:
                        throw new FormatException("Not a Mass Effect Package!");
                }
                FullHeaderSize = fs.ReadInt32();
                int foldernameStrLen = fs.ReadInt32();
                //always "None", so don't bother saving result
                if (foldernameStrLen > 0)
                    fs.ReadStringASCIINull(foldernameStrLen);
                else
                    fs.ReadStringUnicodeNull(foldernameStrLen * -2);

                Flags = (EPackageFlags)fs.ReadUInt32();

                if (Game == MEGame.ME3 && Flags.HasFlag(EPackageFlags.Cooked))
                {
                    unknown1 = fs.ReadInt32();
                }

                NameCount = fs.ReadInt32();
                NameOffset = fs.ReadInt32();
                ExportCount = fs.ReadInt32();
                ExportOffset = fs.ReadInt32();
                ImportCount = fs.ReadInt32();
                ImportOffset = fs.ReadInt32();
                DependencyTableOffset = fs.ReadInt32();

                if (Game == MEGame.ME3)
                {
                    ImportExportGuidsOffset = fs.ReadInt32();
                    ImportGuidsCount = fs.ReadInt32();
                    ExportGuidsCount = fs.ReadInt32();
                    ThumbnailTableOffset = fs.ReadInt32();
                }

                PackageGuid = fs.ReadGuid();
                uint generationsTableCount = fs.ReadUInt32();
                if (generationsTableCount > 0)
                {
                    generationsTableCount--;
                    Gen0ExportCount = fs.ReadInt32();
                    Gen0NameCount = fs.ReadInt32();
                    Gen0NetworkedObjectCount = fs.ReadInt32();
                }
                //should never be more than 1 generation, but just in case
                fs.Skip(generationsTableCount * 12);

                engineVersion = fs.ReadInt32();
                cookedContentVersion = fs.ReadInt32();

                if (Game == MEGame.ME2 || Game == MEGame.ME1)
                {
                    unknown2 = fs.ReadInt32();
                    unknown3 = fs.ReadInt32();
                    unknown4 = fs.ReadInt32();
                    unknown5 = fs.ReadInt32();
                }

                unknown6 = fs.ReadInt32();
                unknown7 = fs.ReadInt32();

                if (Game == MEGame.ME1)
                {
                    unknown8 = fs.ReadInt32();
                }

                PackageCompressionType = (CompressionType)fs.ReadUInt32();
                //skip chunks. Decompressor will handle that
                int numChunks = fs.ReadInt32();
                fs.Skip(numChunks * 16);

                packageSource = fs.ReadUInt32();

                if (Game == MEGame.ME2 || Game == MEGame.ME1)
                {
                    unknown9 = fs.ReadInt32();
                }

                //Doesn't need to be written out, so it doesn't need to be read in
                //keep this here in case one day we learn that this has a purpose
                /*if (Game == MEGame.ME2 || Game == MEGame.ME3)
                {
                    int additionalPackagesToCookCount = fs.ReadInt32();
                    var additionalPackagesToCook = new string[additionalPackagesToCookCount];
                    for (int i = 0; i < additionalPackagesToCookCount; i++)
                    {
                        int strLen = fs.ReadInt32();
                        if (strLen > 0)
                        {
                            additionalPackagesToCook[i] = fs.ReadStringASCIINull(strLen);
                        }
                        else
                        {
                            additionalPackagesToCook[i] = fs.ReadStringUnicodeNull(strLen * -2);
                        }
                    }
                }*/
                #endregion

                Stream inStream = fs;
                if (PackageCompressionType != CompressionType.None && numChunks > 0)
                {
                    inStream = Game == MEGame.ME3 ? CompressionHelper.DecompressME3(fs) : CompressionHelper.DecompressME1orME2(fs);
                }

                //read namelist
                names = new List<string>();
                inStream.JumpTo(NameOffset);
                for (int i = 0; i < NameCount; i++)
                {
                    names.Add(inStream.ReadUnrealString());
                    if (Game == MEGame.ME1)
                        inStream.Skip(8);
                    else if (Game == MEGame.ME2)
                        inStream.Skip(4);
                }

                //read importTable
                imports = new List<ImportEntry>();
                inStream.Seek(ImportOffset, SeekOrigin.Begin);
                for (int i = 0; i < ImportCount; i++)
                {
                    ImportEntry imp = new ImportEntry(this, inStream) { Index = i };
                    imp.PropertyChanged += importChanged;
                    imports.Add(imp);
                }

                //read exportTable (ExportEntry constructor reads export data)
                exports = new List<ExportEntry>();
                inStream.Seek(ExportOffset, SeekOrigin.Begin);
                for (int i = 0; i < ExportCount; i++)
                {
                    ExportEntry e = new ExportEntry(this, inStream) { Index = i };
                    e.PropertyChanged += exportChanged;
                    exports.Add(e);
                }

                if (Game == MEGame.ME1)
                {
                    ReadLocalTLKs();
                }
            }
        }
        public void save()
        {
            save(FilePath);
        }

        public void save(string path)
        {
            //If we're doing save as, IsCompressed should not be changed since we're saving a copy of the file
            if (path == FilePath)
            {
                IsCompressed = false;
            }
            saveByReconstructing(path);
        }


        private void saveByReconstructing(string path)
        {
            try
            {
                var ms = new MemoryStream();
            
                //just for positioning. We write over this later when the header values have been updated
                WriteHeader(ms);

                //name table
                NameOffset = (int)ms.Position;
                NameCount = Gen0NameCount = names.Count;
                foreach (string name in names)
                {
                    switch (Game)
                    {
                        case MEGame.ME1:
                            ms.WriteUnrealStringASCII(name);
                            ms.WriteInt32(0);
                            ms.WriteInt32(458768);
                            break;
                        case MEGame.ME2:
                            ms.WriteUnrealStringASCII(name);
                            ms.WriteInt32(-14);
                            break;
                        case MEGame.ME3:
                            ms.WriteUnrealStringUnicode(name);
                            break;
                    }
                }

                //import table
                ImportOffset = (int)ms.Position;
                ImportCount = imports.Count;
                foreach (ImportEntry e in imports)
                {
                    ms.WriteFromBuffer(e.Header);
                }

                //export table
                ExportOffset = (int)ms.Position;
                ExportCount = Gen0ExportCount = exports.Count;
                foreach (ExportEntry e in exports)
                {
                    e.HeaderOffset = (uint)ms.Position;
                    ms.WriteFromBuffer(e.Header);
                }

                DependencyTableOffset = (int) ms.Position;
                ms.WriteInt32(0);//zero-count DependencyTable
                FullHeaderSize = ImportExportGuidsOffset = (int) ms.Position;

                //export data
                foreach (ExportEntry e in exports)
                {
                    switch (Game)
                    {
                        case MEGame.ME1:
                            UpdateME1Offsets(e, (int)ms.Position);
                            break;
                        case MEGame.ME2:
                            UpdateME2Offsets(e, (int)ms.Position);
                            break;
                        case MEGame.ME3:
                            UpdateME3Offsets(e, (int)ms.Position);
                            break;
                    }

                    e.DataOffset = (int)ms.Position;


                    ms.WriteFromBuffer(e.Data);
                    //update size and offset in already-written header
                    long pos = ms.Position;
                    ms.JumpTo(e.HeaderOffset + 32);
                    ms.WriteInt32(e.DataSize); //DataSize might have been changed by UpdateOffsets
                    ms.WriteInt32(e.DataOffset);
                    ms.JumpTo(pos);
                }

                //re-write header with updated values
                ms.JumpTo(0);
                WriteHeader(ms);


                File.WriteAllBytes(path, ms.ToArray());
                AfterSave();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving {FilePath}:\n{ExceptionHandlerDialogWPF.FlattenException(ex)}");
            }
        }

        private void WriteHeader(Stream ms)
        {
            ms.WriteUInt32(packageTag);
            switch (Game)
            {
                case MEGame.ME1:
                    ms.WriteUInt16(491);
                    ms.WriteUInt16(1008);
                    break;
                case MEGame.ME2:
                    ms.WriteUInt16(512);
                    ms.WriteUInt16(130);
                    break;
                case MEGame.ME3:
                    ms.WriteUInt16(684);
                    ms.WriteUInt16(194);
                    break;
            }
            ms.WriteInt32(FullHeaderSize);
            if (Game == MEGame.ME3)
            {
                ms.WriteUnrealStringUnicode("None");
            }
            else
            {
                ms.WriteUnrealStringASCII("None");
            }
            ms.WriteUInt32((uint)Flags);

            if (Game == MEGame.ME3 && Flags.HasFlag(EPackageFlags.Cooked))
            {
                ms.WriteInt32(unknown1);
            }

            ms.WriteInt32(NameCount);
            ms.WriteInt32(NameOffset);
            ms.WriteInt32(ExportCount);
            ms.WriteInt32(ExportOffset);
            ms.WriteInt32(ImportCount);
            ms.WriteInt32(ImportOffset);
            ms.WriteInt32(DependencyTableOffset);

            if (Game == MEGame.ME3)
            {
                ms.WriteInt32(ImportExportGuidsOffset);
                ms.WriteInt32(ImportGuidsCount);
                ms.WriteInt32(ExportGuidsCount);
                ms.WriteInt32(ThumbnailTableOffset);
            }
            ms.WriteGuid(PackageGuid);

            //Write 1 generation
            ms.WriteInt32(1);
            ms.WriteInt32(Gen0ExportCount);
            ms.WriteInt32(Gen0NameCount);
            ms.WriteInt32(Gen0NetworkedObjectCount);

            ms.WriteInt32(engineVersion);
            ms.WriteInt32(cookedContentVersion);


            if (Game == MEGame.ME2 || Game == MEGame.ME1)
            {
                ms.WriteInt32(unknown2);
                ms.WriteInt32(unknown3);
                ms.WriteInt32(unknown4);
                ms.WriteInt32(unknown5);
            }

            ms.WriteInt32(unknown6);
            ms.WriteInt32(unknown7);

            if (Game == MEGame.ME1)
            {
                ms.WriteInt32(unknown8);
            }

            ms.WriteUInt32((uint)CompressionType.None);
            ms.WriteInt32(0);//numChunks

            ms.WriteUInt32(packageSource);

            if (Game == MEGame.ME2 || Game == MEGame.ME1)
            {
                ms.WriteInt32(unknown9);
            }

            if (Game == MEGame.ME3 || Game == MEGame.ME2)
            {
                ms.WriteInt32(0);//empty additionalPackagesToCook array
            }
        }

        public List<ME1Explorer.Unreal.Classes.TalkFile> LocalTalkFiles { get; } = new List<ME1Explorer.Unreal.Classes.TalkFile>();
        private void ReadLocalTLKs()
        {
            LocalTalkFiles.Clear();
            List<ExportEntry> tlkFileSets = Exports.Where(x => x.ClassName == "BioTlkFileSet" && !x.ObjectName.StartsWith("Default__")).ToList();
            var exportsToLoad = new List<ExportEntry>();
            foreach (var tlkFileSet in tlkFileSets)
            {
                MemoryStream r = new MemoryStream(tlkFileSet.Data);
                r.Position = tlkFileSet.propsEnd();
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int langRef = r.ReadInt32();
                    r.ReadInt32(); //second half of name
                    string lang = getNameEntry(langRef);
                    int numTlksForLang = r.ReadInt32(); //I believe this is always 2. Hopefully I am not wrong.
                    int maleTlk = r.ReadInt32();
                    int femaleTlk = r.ReadInt32();

                    if (Properties.Settings.Default.TLKLanguage.Equals(lang, StringComparison.InvariantCultureIgnoreCase))
                    {
                        exportsToLoad.Add(getUExport(Properties.Settings.Default.TLKGender_IsMale ? maleTlk : femaleTlk));
                        break;
                    }

                    //r.ReadInt64();
                    //talkFiles.Add(new TalkFile(pcc, r.ReadInt32(), true, langRef, index));
                    //talkFiles.Add(new TalkFile(pcc, r.ReadInt32(), false, langRef, index));
                }
            }

            foreach (var exp in exportsToLoad)
            {
                //Debug.WriteLine("Loading local TLK: " + exp.GetIndexedFullPath);
                LocalTalkFiles.Add(new ME1Explorer.Unreal.Classes.TalkFile(exp));
            }
        }

        private static void UpdateME1Offsets(ExportEntry export, int newDataOffset)
        {
            if (export.IsDefaultObject)
            {
                return; //this is not actually instance of that class
            }
            if (export.IsTexture())
            {
                int baseOffset = newDataOffset + export.propsEnd();
                MemoryStream binData = new MemoryStream(export.getBinaryData());
                binData.Skip(12);
                binData.WriteInt32(baseOffset + (int)binData.Position + 4);
                for (int i = binData.ReadInt32(); i > 0 && binData.Position < binData.Length; i--)
                {
                    var storageFlags = (StorageFlags)binData.ReadInt32();
                    if (!storageFlags.HasFlag(StorageFlags.externalFile)) //pcc-stored
                    {
                        int uncompressedSize = binData.ReadInt32();
                        int compressedSize = binData.ReadInt32();
                        binData.WriteInt32(baseOffset + (int)binData.Position + 4);//update offset
                        binData.Seek((storageFlags == StorageFlags.noFlags ? uncompressedSize : compressedSize) + 8, SeekOrigin.Current); //skip texture and width + height values
                    }
                    else
                    {
                        binData.Seek(20, SeekOrigin.Current);//skip whole rest of mip definition
                    }
                }
                export.setBinaryData(binData.ToArray());
            }
            else if (export.ClassName == "StaticMeshComponent")
            {
                int baseOffset = newDataOffset + export.propsEnd();
                MemoryStream bin = new MemoryStream(export.Data);
                bin.JumpTo(export.propsEnd());

                int lodDataCount = bin.ReadInt32();
                for (int i = 0; i < lodDataCount; i++)
                {
                    int shadowMapCount = bin.ReadInt32();
                    bin.Skip(shadowMapCount * 4);
                    int shadowVertCount = bin.ReadInt32();
                    bin.Skip(shadowVertCount * 4);
                    int lightMapType = bin.ReadInt32();
                    if (lightMapType == 0) continue;
                    int lightGUIDsCount = bin.ReadInt32();
                    bin.Skip(lightGUIDsCount * 16);
                    switch (lightMapType)
                    {
                        case 1:
                            bin.Skip(4 + 8);
                            int bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            bin.Skip(12 * 4 + 8);
                            bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            break;
                        case 2:
                            bin.Skip((16) * 4 + 16);
                            break;
                    }
                }
            }
        }

        private static void UpdateME2Offsets(ExportEntry export, int newDataOffset)
        {
            if (export.IsDefaultObject)
            {
                return; //this is not actually instance of that class
            }
            //update offsets for pcc-stored audio in wwisestreams
            if (export.ClassName == "WwiseStream" && export.GetProperty<NameProperty>("Filename") == null)
            {
                byte[] binData = export.getBinaryData();
                if (binData.Length < 44)
                {
                    return; //¯\_(ツ)_ /¯
                }
                binData.OverwriteRange(44, BitConverter.GetBytes(newDataOffset + export.propsEnd() + 48));
                export.setBinaryData(binData);
            }
            //update offsets for pcc-stored mips in Textures
            else if (export.ClassName == "WwiseBank")
            {
                byte[] binData = export.getBinaryData();
                binData.OverwriteRange(20, BitConverter.GetBytes(newDataOffset + export.propsEnd() + 24));
                export.setBinaryData(binData);
            }
            //update offsets for pcc-stored mips in Textures
            else if (export.IsTexture())
            {
                int baseOffset = newDataOffset + export.propsEnd();
                MemoryStream binData = new MemoryStream(export.getBinaryData());
                binData.Skip(12);
                binData.WriteInt32(baseOffset + (int)binData.Position + 4);
                for (int i = binData.ReadInt32(); i > 0 && binData.Position < binData.Length; i--)
                {
                    var storageFlags = (StorageFlags)binData.ReadInt32();
                    if (!storageFlags.HasFlag(StorageFlags.externalFile)) //pcc-stored
                    {
                        int uncompressedSize = binData.ReadInt32();
                        int compressedSize = binData.ReadInt32();
                        binData.WriteInt32(baseOffset + (int)binData.Position + 4);//update offset
                        binData.Seek((storageFlags == StorageFlags.noFlags ? uncompressedSize : compressedSize) + 8, SeekOrigin.Current); //skip texture and width + height values
                    }
                    else
                    {
                        binData.Seek(20, SeekOrigin.Current);//skip whole rest of mip definition
                    }
                }
                export.setBinaryData(binData.ToArray());
            }
            else if (export.ClassName == "ShaderCache")
            {
                int oldDataOffset = export.DataOffset;

                MemoryStream binData = new MemoryStream(export.Data);
                binData.Seek(export.propsEnd() + 1, SeekOrigin.Begin);

                int nameList1Count = binData.ReadInt32();
                binData.Seek(nameList1Count * 12, SeekOrigin.Current);

                int shaderCount = binData.ReadInt32();
                for (int i = 0; i < shaderCount; i++)
                {
                    binData.Seek(24, SeekOrigin.Current);
                    int nextShaderOffset = binData.ReadInt32() - oldDataOffset;
                    binData.Seek(-4, SeekOrigin.Current);
                    binData.WriteInt32(nextShaderOffset + newDataOffset);
                    binData.Seek(nextShaderOffset, SeekOrigin.Begin);
                }

                int vertexFactoryMapCount = binData.ReadInt32();
                binData.Seek(vertexFactoryMapCount * 12, SeekOrigin.Current);

                int materialShaderMapCount = binData.ReadInt32();
                for (int i = 0; i < materialShaderMapCount; i++)
                {
                    binData.Seek(16, SeekOrigin.Current);

                    int switchParamCount = binData.ReadInt32();
                    binData.Seek(switchParamCount * 32, SeekOrigin.Current);

                    int componentMaskParamCount = binData.ReadInt32();
                    binData.Seek(componentMaskParamCount * 44, SeekOrigin.Current);

                    int nextMaterialShaderMapOffset = binData.ReadInt32() - oldDataOffset;
                    binData.Seek(-4, SeekOrigin.Current);
                    binData.WriteInt32(nextMaterialShaderMapOffset + newDataOffset);
                    binData.Seek(nextMaterialShaderMapOffset, SeekOrigin.Begin);
                }

                export.Data = binData.ToArray();
            }
            else if (export.ClassName == "StaticMeshComponent")
            {
                int baseOffset = newDataOffset + export.propsEnd();
                MemoryStream bin = new MemoryStream(export.Data);
                bin.JumpTo(export.propsEnd());

                int lodDataCount = bin.ReadInt32();
                for (int i = 0; i < lodDataCount; i++)
                {
                    int shadowMapCount = bin.ReadInt32();
                    bin.Skip(shadowMapCount * 4);
                    int shadowVertCount = bin.ReadInt32();
                    bin.Skip(shadowVertCount * 4);
                    int lightMapType = bin.ReadInt32();
                    if (lightMapType == 0) continue;
                    int lightGUIDsCount = bin.ReadInt32();
                    bin.Skip(lightGUIDsCount * 16);
                    switch (lightMapType)
                    {
                        case 1:
                            bin.Skip(4 + 8);
                            int bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            bin.Skip(12 * 4 + 8);
                            bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            break;
                        case 2:
                            bin.Skip((16) * 4 + 16);
                            break;
                    }
                }
            }
        }

        private static void UpdateME3Offsets(ExportEntry export, int newDataOffset)
        {
            if (export.IsDefaultObject)
            {
                return; //this is not actually instance of that class
            }
            //update offsets for pcc-stored audio in wwisestreams
            if ((export.ClassName == "WwiseStream" && export.GetProperty<NameProperty>("Filename") == null) || export.ClassName == "WwiseBank")
            {
                byte[] binData = export.getBinaryData();
                binData.OverwriteRange(12, BitConverter.GetBytes(newDataOffset + export.propsEnd() + 16));
                export.setBinaryData(binData);
            }
            //update offsets for pcc-stored movies in texturemovies
            else if (export.ClassName == "TextureMovie" && export.GetProperty<NameProperty>("TextureFileCacheName") == null)
            {
                byte[] binData = export.getBinaryData();
                binData.OverwriteRange(12, BitConverter.GetBytes(newDataOffset + export.propsEnd() + 16));
                export.setBinaryData(binData);
            }
            //update offsets for pcc-stored mips in Textures
            else if (export.IsTexture())
            {
                int baseOffset = newDataOffset + export.propsEnd();
                MemoryStream binData = new MemoryStream(export.getBinaryData());
                for (int i = binData.ReadInt32(); i > 0 && binData.Position < binData.Length; i--)
                {
                    if (binData.ReadInt32() == 0) //pcc-stored
                    {
                        int uncompressedSize = binData.ReadInt32();
                        binData.Seek(4, SeekOrigin.Current); //skip compressed size
                        binData.WriteInt32(baseOffset + (int)binData.Position + 4);//update offset
                        binData.Seek(uncompressedSize + 8, SeekOrigin.Current); //skip texture and width + height values
                    }
                    else
                    {
                        binData.Seek(20, SeekOrigin.Current);//skip whole rest of mip definition
                    }
                }
                export.setBinaryData(binData.ToArray());
            }
            else if (export.ClassName == "ShaderCache")
            {
                int oldDataOffset = export.DataOffset;

                MemoryStream binData = new MemoryStream(export.Data);
                binData.Seek(export.propsEnd() + 1, SeekOrigin.Begin);

                int nameList1Count = binData.ReadInt32();
                binData.Seek(nameList1Count * 12, SeekOrigin.Current);

                int namelist2Count = binData.ReadInt32();//namelist2
                binData.Seek(namelist2Count * 12, SeekOrigin.Current);

                int shaderCount = binData.ReadInt32();
                for (int i = 0; i < shaderCount; i++)
                {
                    binData.Seek(24, SeekOrigin.Current);
                    int nextShaderOffset = binData.ReadInt32() - oldDataOffset;
                    binData.Seek(-4, SeekOrigin.Current);
                    binData.WriteInt32(nextShaderOffset + newDataOffset);
                    binData.Seek(nextShaderOffset, SeekOrigin.Begin);
                }

                int vertexFactoryMapCount = binData.ReadInt32();
                binData.Seek(vertexFactoryMapCount * 12, SeekOrigin.Current);

                int materialShaderMapCount = binData.ReadInt32();
                for (int i = 0; i < materialShaderMapCount; i++)
                {
                    binData.Seek(16, SeekOrigin.Current);

                    int switchParamCount = binData.ReadInt32();
                    binData.Seek(switchParamCount * 32, SeekOrigin.Current);

                    int componentMaskParamCount = binData.ReadInt32();
                    binData.Seek(componentMaskParamCount * 44, SeekOrigin.Current);

                    int normalParams = binData.ReadInt32();
                    binData.Seek(normalParams * 29, SeekOrigin.Current);

                    binData.Seek(8, SeekOrigin.Current);

                    int nextMaterialShaderMapOffset = binData.ReadInt32() - oldDataOffset;
                    binData.Seek(-4, SeekOrigin.Current);
                    binData.WriteInt32(nextMaterialShaderMapOffset + newDataOffset);
                    binData.Seek(nextMaterialShaderMapOffset, SeekOrigin.Begin);
                }

                export.Data = binData.ToArray();
            }
            else if (export.ClassName == "StaticMeshComponent")
            {
                int baseOffset = newDataOffset + export.propsEnd();
                MemoryStream bin = new MemoryStream(export.Data);
                bin.JumpTo(export.propsEnd());

                int lodDataCount = bin.ReadInt32();
                for (int i = 0; i < lodDataCount; i++)
                {
                    int shadowMapCount = bin.ReadInt32();
                    bin.Skip(shadowMapCount * 4);
                    int shadowVertCount = bin.ReadInt32();
                    bin.Skip(shadowVertCount * 4);
                    int lightMapType = bin.ReadInt32();
                    if (lightMapType == 0) continue;
                    int lightGUIDsCount = bin.ReadInt32();
                    bin.Skip(lightGUIDsCount * 16);
                    int bulkDataSize;
                    switch (lightMapType)
                    {
                        case 1:
                            bin.Skip(4 + 8);
                            bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            bin.Skip(12 * 3 + 8);
                            bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            break;
                        case 2:
                            bin.Skip((16) * 3 + 16);
                            break;
                        case 3:
                            bin.Skip(8);
                            bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            bin.Skip(24);
                            break;
                        case 4:
                        case 6:
                            bin.Skip(124);
                            break;
                        case 5:
                            bin.Skip(4 + 8);
                            bulkDataSize = bin.ReadInt32();
                            bin.WriteInt32(baseOffset + (int)bin.Position + 4);
                            bin.Skip(bulkDataSize);
                            bin.Skip(12);
                            break;
                    }
                }
            }
        }

    }
}