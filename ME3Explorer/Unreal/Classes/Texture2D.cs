﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Drawing;
using ME3Explorer.Unreal;
using ME3Explorer.Packages;
using Gibbed.IO;
using AmaroK86.ImageFormat;
using AmaroK86.MassEffect3.ZlibBlock;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace ME3Explorer.Unreal.Classes
{
    public class Texture2D
    {

        //TODO: Replace this with Texture2DMipInfo
        public struct ImageInfo
        {
            public StorageTypes storageType;
            public int uncSize;
            public int cprSize;
            public int offset;
            public uint inExportDataOffset; //This is only used for PCC stored
            public ImageSize imgSize;
        }

        readonly IMEPackage pccRef;
        private readonly ExportEntry textureExport;
        public const string className = "Texture2D";
        public string texName { get; }
        public string arcName { get; }
        private readonly string texFormat;
        private readonly byte[] imageData;
        public uint pccOffset;
        public List<ImageInfo> imgList { get; } // showable image list

        public Texture2D(IMEPackage pccObj, int texIdx)
        {
            pccRef = pccObj;
            // check if texIdx is an Export index and a Texture2D class
            if (pccObj.isUExport(texIdx + 1) && pccObj.getExport(texIdx).ClassName == className)
            {
                textureExport = pccObj.getExport(texIdx);
                pccOffset = (uint)textureExport.DataOffset;
                texName = textureExport.ObjectName;

                texFormat = textureExport.GetProperty<EnumProperty>("Format")?.Value.Name.Substring(3) ?? "";
                arcName = textureExport.GetProperty<NameProperty>("TextureFileCacheName")?.Value.Name ?? "";
                int dataOffset = textureExport.propsEnd();
                // if "None" property isn't found throws an exception
                if (dataOffset == 0)
                    throw new Exception("\"None\" property not found");
                imageData = textureExport.Data;
            }
            else
                throw new Exception($"Texture2D {texIdx} not found");

            MemoryStream dataStream = new MemoryStream(imageData);
            dataStream.Position = textureExport.propsEnd(); //scroll to binary
            if (pccObj.Game != MEGame.ME3)
            {
                dataStream.Position += 16; //12 zeros, file offset
            }
            uint numMipMaps = dataStream.ReadValueU32();
            uint count = numMipMaps;

            imgList = new List<ImageInfo>();
            while (dataStream.Position < dataStream.Length && count > 0)
            {
                ImageInfo imgInfo = new ImageInfo
                {
                    storageType = (StorageTypes)dataStream.ReadValueS32(),
                    uncSize = dataStream.ReadValueS32(),
                    cprSize = dataStream.ReadValueS32(),
                    offset = dataStream.ReadValueS32(),
                    inExportDataOffset = (uint)dataStream.Position
                };
                if (imgInfo.storageType == StorageTypes.pccUnc)
                {
                    //imgInfo.offset = (int)(pccOffset + dataOffset); // saving pcc offset as relative to exportdata offset, not absolute
                    imgInfo.offset = (int)dataStream.Position; // saving pcc offset as relative to exportdata offset, not absolute
                    //MessageBox.Show("Pcc class offset: " + pccOffset + "\nimages data offset: " + imgInfo.offset.ToString());
                    dataStream.Seek(imgInfo.uncSize, SeekOrigin.Current);
                }
                else if (imgInfo.storageType == StorageTypes.pccLZO || imgInfo.storageType == StorageTypes.pccZlib)
                {
                    dataStream.Seek(imgInfo.cprSize, SeekOrigin.Current);
                }
                imgInfo.imgSize = new ImageSize(dataStream.ReadValueU32(), dataStream.ReadValueU32());

                /* We might want to implement this. this is from mem code
                if (mip.width == 4 && mips.Exists(m => m.width == mip.width))
                    mip.width = mips.Last().width / 2;
                if (mip.height == 4 && mips.Exists(m => m.height == mip.height))
                    mip.height = mips.Last().height / 2;
                if (mip.width == 0)
                    mip.width = 1;
                if (mip.height == 0)
                    mip.height = 1;
                 */

                imgList.Add(imgInfo);
                count--;
            }

            // save what remains
            /*int remainingBytes = (int)(dataStream.Length - dataStream.Position);
            footerData = new byte[remainingBytes];
            dataStream.Read(footerData, 0, footerData.Length);*/
        }

        public static string GetTFC(string arcname, MEGame game)
        {
            if (!arcname.EndsWith(".tfc"))
                arcname += ".tfc";

            foreach (string s in MELoadedFiles.GetEnabledDLC(game).OrderBy(dir => MELoadedFiles.GetMountPriority(dir, game)).Append(MEDirectories.BioGamePath(game)))
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(s, game == MEGame.ME2 ? "CookedPC" : "CookedPCConsole")))
                {
                    if (Path.GetFileName(file) == arcname)
                    {
                        return file;
                    }
                }
            }
            return "";
        }

        public byte[] extractRawData(ImageInfo imgInfo, IMEPackage package = null)
        {
            byte[] imgBuffer;
            string archiveDir = null;
            if (package != null) archiveDir = Path.GetDirectoryName(package.FilePath);
            switch (imgInfo.storageType)
            {
                case StorageTypes.pccUnc:
                    imgBuffer = new byte[imgInfo.uncSize];
                    System.Buffer.BlockCopy(imageData, imgInfo.offset, imgBuffer, 0, imgInfo.uncSize);
                    break;
                case StorageTypes.pccLZO:
                case StorageTypes.pccZlib:
                    imgBuffer = new byte[imgInfo.uncSize];
                    using (MemoryStream tmpStream = new MemoryStream(textureExport.Data, (int)imgInfo.inExportDataOffset, imgInfo.cprSize)) //pcc stored don't use the direct offsets
                    {
                        try
                        {
                            TextureCompression.DecompressTexture(imgBuffer, tmpStream, imgInfo.storageType, imgInfo.uncSize, imgInfo.cprSize);
                        }
                        catch (Exception e)
                        {
                            throw new Exception(e.Message + "\nError decompressing texture.");
                        }
                    }


                    break;
                case StorageTypes.extUnc:
                case StorageTypes.extZlib:
                case StorageTypes.extLZO:
                    string archivePath;
                    imgBuffer = new byte[imgInfo.uncSize];
                    if (archiveDir != null && File.Exists(Path.Combine(archiveDir, arcName)))
                    {
                        archivePath = Path.Combine(archiveDir, arcName);
                    }
                    else
                    {
                        archivePath = GetTFC(arcName, package.Game);
                    }
                    if (archivePath != null && File.Exists(archivePath))
                    {
                        Debug.WriteLine($"Loading texture from tfc '{archivePath}'.");
                        try
                        {
                            using (FileStream archiveStream = File.OpenRead(archivePath))
                            {
                                archiveStream.Seek(imgInfo.offset, SeekOrigin.Begin);
                                if (imgInfo.storageType == StorageTypes.extZlib || imgInfo.storageType == StorageTypes.extLZO)
                                {

                                    using (MemoryStream tmpStream = new MemoryStream(archiveStream.ReadBytes(imgInfo.cprSize)))
                                    {
                                        try
                                        {
                                            TextureCompression.DecompressTexture(imgBuffer, tmpStream, imgInfo.storageType, imgInfo.uncSize, imgInfo.cprSize);
                                        }
                                        catch (Exception e)
                                        {
                                            throw new Exception(e.Message + "\n" + "File: " + archivePath + "\n" +
                                                                "StorageType: " + imgInfo.storageType + "\n" +
                                                                "External file offset: " + imgInfo.offset);
                                        }
                                    }
                                }
                                else
                                {
                                    archiveStream.Read(imgBuffer, 0, imgBuffer.Length);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            //how do i put default unreal texture
                            imgBuffer = null; //this will cause exception that will bubble up.
                            throw new Exception(e.Message + "\n" + "File: " + archivePath + "\n" +
                                                "StorageType: " + imgInfo.storageType + "\n" +
                                                "External file offset: " + imgInfo.offset);
                        }
                    }
                    break;
                default:
                    throw new FormatException("Unsupported texture storage type: " + imgInfo.storageType);
            }
            return imgBuffer; //cannot be uninitialized.
        }

        // Creates a Direct3D texture that looks like this one.
        public SharpDX.Direct3D11.Texture2D generatePreviewTexture(Device device, out Texture2DDescription description)
        {
            ImageInfo info = new ImageInfo();
            info = imgList.FirstOrDefault(x => x.storageType != StorageTypes.empty);
            if (info.imgSize == null)
            {
                description = new Texture2DDescription();
                return null;
            }

            int width = (int)info.imgSize.width;
            int height = (int)info.imgSize.height;
            Debug.WriteLine($"Generating preview texture for Texture2D of format {texFormat}");

            // Convert compressed image data to an A8R8G8B8 System.Drawing.Bitmap
            DDSFormat format;
            const Format dxformat = Format.B8G8R8A8_UNorm;
            switch (texFormat)
            {
                case "DXT1":
                    format = DDSFormat.DXT1;
                    break;
                case "DXT5":
                    format = DDSFormat.DXT5;
                    break;
                case "V8U8":
                    format = DDSFormat.V8U8;
                    break;
                case "G8":
                    format = DDSFormat.G8;
                    break;
                case "A8R8G8B8":
                    format = DDSFormat.ARGB;
                    break;
                case "NormalMap_HQ":
                    format = DDSFormat.ATI2;
                    break;
                default:
                    throw new FormatException("Unknown texture format: " + texFormat);
            }

            byte[] compressedData = extractRawData(info, pccRef);
            Bitmap bmp = DDSImage.ToBitmap(compressedData, format, width, height);

            // Load the decompressed data into an array
            System.Drawing.Imaging.BitmapData data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var pixels = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(data);

            // Create description of texture
            description.Width = width;
            description.Height = height;
            description.MipLevels = 1;
            description.ArraySize = 1;
            description.Format = dxformat;
            description.SampleDescription.Count = 1;
            description.SampleDescription.Quality = 0;
            description.Usage = ResourceUsage.Default;
            description.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget;
            description.CpuAccessFlags = 0;
            description.OptionFlags = ResourceOptionFlags.GenerateMipMaps;

            // Set up the texture data
            int stride = width * 4;
            DataStream ds = new DataStream(height * stride, true, true);
            ds.Write(pixels, 0, height * stride);
            ds.Position = 0;
            // Create texture
            SharpDX.Direct3D11.Texture2D tex = new SharpDX.Direct3D11.Texture2D(device, description, new DataRectangle(ds.DataPointer, stride));
            ds.Dispose();

            return tex;
        }
    }
}
