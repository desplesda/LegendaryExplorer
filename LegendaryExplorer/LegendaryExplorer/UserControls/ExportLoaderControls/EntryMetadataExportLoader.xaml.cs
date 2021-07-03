﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Be.Windows.Forms;
using LegendaryExplorer.Dialogs;
using LegendaryExplorer.Misc;
using LegendaryExplorer.SharedUI;
using LegendaryExplorerCore.Gammtek.IO;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using Xceed.Wpf.Toolkit.Primitives;
using static LegendaryExplorerCore.Unreal.UnrealFlags;

namespace LegendaryExplorer.UserControls.ExportLoaderControls
{
    /// <summary>
    /// Interaction logic for MetadataEditorWPF.xaml
    /// </summary>
    public partial class EntryMetadataExportLoader : ExportLoaderControl
    {
        //This is a ExportLoaderControl as it can technically function as one. It can also function as an ImportLoader. Given that there is really no other
        //use for loading imports into an editor I am going to essentially just add the required load methods in this loader.

        private const int HEADER_OFFSET_EXP_IDXCLASS = 0x0;
        private const int HEADER_OFFSET_EXP_IDXSUPERCLASS = 0x4;
        private const int HEADER_OFFSET_EXP_IDXLINK = 0x8;
        private const int HEADER_OFFSET_EXP_IDXOBJECTNAME = 0xC;
        private const int HEADER_OFFSET_EXP_INDEXVALUE = 0x10;
        private const int HEADER_OFFSET_EXP_IDXARCHETYPE = 0x14;
        private const int HEADER_OFFSET_EXP_OBJECTFLAGS = 0x18;

        private const int HEADER_OFFSET_EXP_UNKNOWN1 = 0x1C;


        private const int HEADER_OFFSET_IMP_IDXCLASSNAME = 0x8;
        private const int HEADER_OFFSET_IMP_IDXLINK = 0x10;
        private const int HEADER_OFFSET_IMP_IDXOBJECTNAME = 0x14;
        private const int HEADER_OFFSET_IMP_IDXPACKAGEFILE = 0x0;
        private IEntry _currentLoadedEntry;
        public IEntry CurrentLoadedEntry { get => _currentLoadedEntry; private set => SetProperty(ref _currentLoadedEntry, value); }
        private byte[] OriginalHeader;

        public ObservableCollectionExtended<object> AllEntriesList { get; } = new();
        public int CurrentObjectNameIndex { get; private set; }

        private HexBox Header_Hexbox;
        private ReadOptimizedByteProvider headerByteProvider;
        private bool loadingNewData;

        public bool SubstituteImageForHexBox
        {
            get => (bool)GetValue(SubstituteImageForHexBoxProperty);
            set => SetValue(SubstituteImageForHexBoxProperty, value);
        }
        public static readonly DependencyProperty SubstituteImageForHexBoxProperty = DependencyProperty.Register(
            nameof(SubstituteImageForHexBox), typeof(bool), typeof(EntryMetadataExportLoader), new PropertyMetadata(false, SubstituteImageForHexBoxChangedCallback));

        private static void SubstituteImageForHexBoxChangedCallback(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            EntryMetadataExportLoader i = (EntryMetadataExportLoader)obj;
            if (e.NewValue is true && i.Header_Hexbox_Host.Child.Height > 0 && i.Header_Hexbox_Host.Child.Width > 0)
            {
                i.hexboxImageSub.Source = i.Header_Hexbox_Host.Child.DrawToBitmapSource();
                i.hexboxImageSub.Width = i.Header_Hexbox_Host.ActualWidth;
                i.hexboxImageSub.Height = i.Header_Hexbox_Host.ActualHeight;
                i.hexboxImageSub.Visibility = Visibility.Visible;
                i.Header_Hexbox_Host.Visibility = Visibility.Collapsed;
            }
            else
            {
                i.Header_Hexbox_Host.Visibility = Visibility.Visible;
                i.hexboxImageSub.Visibility = Visibility.Collapsed;
            }
        }

        public string ObjectIndexOffsetText => CurrentLoadedEntry is ImportEntry ? "0x18 Object index:" : "0x10 Object index:";

        public EntryMetadataExportLoader() : base("Metadata Editor")
        {
            DataContext = this;
            LoadCommands();
            InitializeComponent();
        }

        private bool ControlLoaded;

        private bool _hexChanged;

        public bool HexChanged
        {
            get => _hexChanged && CurrentLoadedEntry != null;
            private set
            {
                if (SetProperty(ref _hexChanged, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ICommand SaveHexChangesCommand { get; private set; }

        private void LoadCommands()
        {
            SaveHexChangesCommand = new GenericCommand(SaveHexChanges, CanSaveHexChanges);
        }

        private bool CanSaveHexChanges()
        {
            if (CurrentLoadedEntry == null || !HexChanged) return false;

            return true;
        }

        private void SaveHexChanges()
        {
            var m = new MemoryStream();
            for (int i = 0; i < headerByteProvider.Length; i++)
                m.WriteByte(headerByteProvider.ReadByte(i));
            CurrentLoadedEntry.Header = m.ToArray();
            switch (CurrentLoadedEntry)
            {
                case ExportEntry exportEntry:
                    LoadExport(exportEntry);
                    break;
                case ImportEntry importEntry:
                    LoadImport(importEntry);
                    break;
            }
        }

        public override bool CanParse(ExportEntry exportEntry) => true;

        public void RefreshAllEntriesList(IMEPackage pcc)
        {
            if (pcc is null)
            {
                AllEntriesList.ClearEx();
                return;
            }
            var allEntriesNew = new List<object>();
            for (int i = pcc.Imports.Count - 1; i >= 0; i--)
            {
                allEntriesNew.Add(pcc.Imports[i]);
            }
            allEntriesNew.Add(ZeroUIndexClassEntry.instance);
            foreach (ExportEntry exp in pcc.Exports)
            {
                allEntriesNew.Add(exp);
            }
            AllEntriesList.ReplaceAll(allEntriesNew);
        }

        public override void PopOut()
        {
            if (CurrentLoadedEntry is ExportEntry export)
            {
                var mde = new EntryMetadataExportLoader();
                var elhw = new ExportLoaderHostedWindow(mde, export)
                {
                    Height = 620,
                    Width = 780,
                    Title = $"Metadata Editor - {export.UIndex} {export.InstancedFullPath} - {export.FileRef.FilePath}"
                };
                mde.RefreshAllEntriesList(CurrentLoadedEntry.FileRef);
                elhw.Show();
            }
        }

        public override void LoadExport(ExportEntry exportEntry)
        {
            loadingNewData = true;
            try
            {
                Row_Archetype.Height = new GridLength(24);
                Row_ExpClass.Height = new GridLength(24);
                Row_Superclass.Height = new GridLength(24);
                Row_ImpClass.Height = new GridLength(0);
                Row_ExpClass.Height = new GridLength(24);
                Row_Packagefile.Height = new GridLength(0);
                Row_ObjectFlags.Height = new GridLength(24);
                Row_ExportDataSize.Height = new GridLength(24);
                Row_ExportDataOffsetDec.Height = new GridLength(24);
                Row_ExportDataOffsetHex.Height = new GridLength(24);
                Row_ExportExportFlags.Height = new GridLength(24);
                Row_ExportPackageFlags.Height = new GridLength(24);
                Row_ExportGenerationNetObjectCount.Height = new GridLength(24);
                Row_ExportGUID.Height = new GridLength(24);
                InfoTab_Link_TextBlock.Text = "0x08 Link:";
                InfoTab_ObjectName_TextBlock.Text = "0x0C Object name:";

                InfoTab_Objectname_ComboBox.SelectedIndex = exportEntry.FileRef.findName(exportEntry.ObjectName.Name);

                LoadAllEntriesBindedItems(exportEntry);

                InfoTab_Headersize_TextBox.Text = $"{exportEntry.Header.Length} bytes";
                InfoTab_ObjectnameIndex_TextBox.Text = exportEntry.indexValue.ToString();

                var flagsList = Enums.GetValues<EObjectFlags>().Distinct().ToList();
                //Don't even get me started on how dumb it is that SelectedItems is read only...
                string selectedFlags = flagsList.Where(flag => exportEntry.ObjectFlags.HasFlag(flag)).StringJoin(" ");

                InfoTab_Flags_ComboBox.ItemsSource = flagsList;
                InfoTab_Flags_ComboBox.SelectedValue = selectedFlags;

                InfoTab_ExportDataSize_TextBox.Text =
                    $"{exportEntry.DataSize} bytes ({FileSize.FormatSize(exportEntry.DataSize)})";
                InfoTab_ExportOffsetHex_TextBox.Text = $"0x{exportEntry.DataOffset:X8}";
                InfoTab_ExportOffsetDec_TextBox.Text = exportEntry.DataOffset.ToString();

                if (exportEntry.HasComponentMap)
                {
                    OrderedMultiValueDictionary<NameReference, int> componentMap = exportEntry.ComponentMap;
                    string components = $"ComponentMap: 0x{40:X2} {componentMap.Count} items\n";
                    int pairOffset = 44;
                    foreach ((NameReference name, int uIndex) in componentMap)
                    {
                        components += $"0x{pairOffset:X2} {name.Instanced} => {uIndex} {exportEntry.FileRef.GetEntryString(uIndex + 1)}\n"; // +1 because it appears to be 0 based?
                        pairOffset += 12;
                    }

                    Header_Hexbox_ComponentsLabel.Text = components;
                }
                else
                {
                    Header_Hexbox_ComponentsLabel.Text = "";
                }

                InfoTab_ExportFlags_TextBlock.Text = $"0x{exportEntry.ExportFlagsOffset:X2} ExportFlags:";
                InfoTab_ExportFlags_TextBox.Text = Enums.GetValues<EExportFlags>().Distinct().ToList()
                    .Where(flag => exportEntry.ExportFlags.HasFlag(flag)).StringJoin(" ");

                InfoTab_GenerationNetObjectCount_TextBlock.Text =
                    $"0x{exportEntry.ExportFlagsOffset + 4:X2} GenerationNetObjs:";
                int[] generationNetObjectCount = exportEntry.GenerationNetObjectCount;
                InfoTab_GenerationNetObjectCount_TextBox.Text =
                    $"{generationNetObjectCount.Length} counts: {string.Join(", ", generationNetObjectCount)}";

                InfoTab_GUID_TextBlock.Text = $"0x{exportEntry.PackageGuidOffset:X2} GUID:";
                InfoTab_ExportGUID_TextBox.Text = exportEntry.PackageGUID.ToString();
                if (exportEntry.FileRef.Platform == MEPackage.GamePlatform.PC)
                {

                    InfoTab_PackageFlags_TextBlock.Text = $"0x{exportEntry.PackageGuidOffset + 16:X2} PackageFlags:";
                    InfoTab_PackageFlags_TextBox.Text = Enums.GetValues<EPackageFlags>().Distinct().ToList()
                        .Where(flag => exportEntry.PackageFlags.HasFlag(flag)).StringJoin(" ");
                }
                else
                {
                    InfoTab_PackageFlags_TextBlock.Text = "";
                    InfoTab_PackageFlags_TextBox.Text = "";
                }
            }
            catch (Exception e)
            {
                //MessageBox.Show("An error occurRed while attempting to read the header for this export. This indicates there is likely something wrong with the header or its parent header.\n\n" + e.Message);
            }

            CurrentLoadedEntry = exportEntry;
            OriginalHeader = CurrentLoadedEntry.Header;
            headerByteProvider.ReplaceBytes(CurrentLoadedEntry.Header);
            HexChanged = false;
            Header_Hexbox.Refresh();
            OnPropertyChanged(nameof(ObjectIndexOffsetText));
            loadingNewData = false;
        }

        /// <summary>
        /// Sets the dropdowns for the items binded to the AllEntries list. HandleUpdate() may fire in the parent control, refreshing the list of values, so we will refire this when that occurs.
        /// </summary>
        /// <param name="entry"></param>
        private void LoadAllEntriesBindedItems(IEntry entry)
        {
            if (entry is ExportEntry exportEntry)
            {
                if (exportEntry.IsClass)
                {
                    InfoTab_Class_ComboBox.SelectedItem = ZeroUIndexClassEntry.instance; //Class, 0
                }
                else
                {
                    InfoTab_Class_ComboBox.SelectedItem = exportEntry.Class; //make positive
                }

                if (exportEntry.HasSuperClass)
                {
                    InfoTab_Superclass_ComboBox.SelectedItem = exportEntry.SuperClass;
                }
                else
                {
                    InfoTab_Superclass_ComboBox.SelectedItem = ZeroUIndexClassEntry.instance; //Class, 0
                }

                if (exportEntry.HasParent)
                {
                    InfoTab_PackageLink_ComboBox.SelectedItem = exportEntry.Parent;
                }
                else
                {
                    InfoTab_PackageLink_ComboBox.SelectedItem = ZeroUIndexClassEntry.instance; //Class, 0
                }

                if (exportEntry.HasArchetype)
                {
                    InfoTab_Archetype_ComboBox.SelectedItem = exportEntry.Archetype;
                }
                else
                {
                    InfoTab_Archetype_ComboBox.SelectedItem = ZeroUIndexClassEntry.instance; //Class, 0
                }
            }
            else if (entry is ImportEntry importEntry)
            {
                if (importEntry.HasParent)
                {
                    InfoTab_PackageLink_ComboBox.SelectedItem = importEntry.Parent;
                }
                else
                {
                    InfoTab_PackageLink_ComboBox.SelectedItem = ZeroUIndexClassEntry.instance; //Class, 0
                }
            }
        }

        public void LoadImport(ImportEntry importEntry)
        {
            loadingNewData = true;
            InfoTab_Headersize_TextBox.Text = $"{importEntry.Header.Length} bytes";
            Row_Archetype.Height = new GridLength(0);
            Row_ExpClass.Height = new GridLength(0);
            Row_ImpClass.Height = new GridLength(24);
            Row_ExportDataSize.Height = new GridLength(0);
            Row_ExportDataOffsetDec.Height = new GridLength(0);
            Row_ExportDataOffsetHex.Height = new GridLength(0);
            Row_ExportExportFlags.Height = new GridLength(0);
            Row_ExportPackageFlags.Height = new GridLength(0);
            Row_ExportGenerationNetObjectCount.Height = new GridLength(0);
            Row_ExportGUID.Height = new GridLength(0);
            Row_Superclass.Height = new GridLength(0);
            Row_ObjectFlags.Height = new GridLength(0);
            Row_Packagefile.Height = new GridLength(24);
            InfoTab_Link_TextBlock.Text = "0x10 Link:";
            InfoTab_ObjectName_TextBlock.Text = "0x14 Object name:";
            Header_Hexbox_ComponentsLabel.Text = "";

            InfoTab_Objectname_ComboBox.SelectedIndex = importEntry.FileRef.findName(importEntry.ObjectName.Name);
            InfoTab_ImpClass_ComboBox.SelectedIndex = importEntry.FileRef.findName(importEntry.ClassName);
            LoadAllEntriesBindedItems(importEntry);

            InfoTab_PackageFile_ComboBox.SelectedIndex = importEntry.FileRef.findName(importEntry.PackageFile);
            InfoTab_ObjectnameIndex_TextBox.Text = importEntry.indexValue.ToString();
            CurrentLoadedEntry = importEntry;
            OriginalHeader = CurrentLoadedEntry.Header;
            headerByteProvider.ReplaceBytes(CurrentLoadedEntry.Header);
            Header_Hexbox.Refresh();
            HexChanged = false;
            OnPropertyChanged(nameof(ObjectIndexOffsetText));
            loadingNewData = false;
        }

        internal void SetHexboxSelectedOffset(long v)
        {
            if (Header_Hexbox != null)
            {
                Header_Hexbox.SelectionStart = v;
                Header_Hexbox.SelectionLength = 1;
            }
        }

        private void hb1_SelectionChanged(object sender, EventArgs e)
        {
            int start = (int)Header_Hexbox.SelectionStart;
            int len = (int)Header_Hexbox.SelectionLength;
            int size = (int)headerByteProvider.Length;

            var currentData = headerByteProvider.Span;
            try
            {
                if (start != -1 && start < size)
                {
                    string s = $"Byte: {currentData[start]}"; //if selection is same as size this will crash.
                    if (start <= currentData.Length - 4)
                    {
                        int val = EndianReader.ToInt32(currentData, start, CurrentLoadedEntry.FileRef.Endian);
                        s += $", Int: {val}";
                        if (CurrentLoadedEntry.FileRef.IsName(val))
                        {
                            s += $", Name: {CurrentLoadedEntry.FileRef.GetNameEntry(val)}";
                        }
                        if (CurrentLoadedEntry.FileRef.GetEntry(val) is ExportEntry exp)
                        {
                            s += $", Export: {exp.ObjectName.Instanced}";
                        }
                        else if (CurrentLoadedEntry.FileRef.GetEntry(val) is ImportEntry imp)
                        {
                            s += $", Import: {imp.ObjectName.Instanced}";
                        }
                    }
                    s += $" | Start=0x{start:X8} ";
                    if (len > 0)
                    {
                        s += $"Length=0x{len:X8} ";
                        s += $"End=0x{start + len - 1:X8}";
                    }
                    Header_Hexbox_SelectedBytesLabel.Text = s;
                }
                else
                {
                    Header_Hexbox_SelectedBytesLabel.Text = "Nothing Selected";
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        internal void ClearMetadataPane()
        {
            loadingNewData = true;
            InfoTab_Objectname_ComboBox.SelectedItem = null;
            InfoTab_Class_ComboBox.SelectedItem = null;
            InfoTab_Superclass_ComboBox.SelectedItem = null;
            InfoTab_PackageLink_ComboBox.SelectedItem = null;
            InfoTab_Headersize_TextBox.Text = null;
            InfoTab_ObjectnameIndex_TextBox.Text = null;
            //InfoTab_Archetype_ComboBox.ItemsSource = null;
            //InfoTab_Archetype_ComboBox.Items.Clear();
            InfoTab_Archetype_ComboBox.SelectedItem = null;
            InfoTab_Flags_ComboBox.ItemsSource = null;
            InfoTab_Flags_ComboBox.SelectedItem = null;
            InfoTab_ExportDataSize_TextBox.Text = null;
            InfoTab_ExportOffsetHex_TextBox.Text = null;
            InfoTab_ExportOffsetDec_TextBox.Text = null;
            headerByteProvider.Clear();
            loadingNewData = false;
        }


        public override void UnloadExport()
        {
            UnloadEntry();
        }

        private void UnloadEntry()
        {
            CurrentLoadedEntry = null;
            ClearMetadataPane();
            Header_Hexbox?.Refresh();
        }

        internal void LoadPccData(IMEPackage pcc)
        {
            RefreshAllEntriesList(pcc);
        }

        //Exports
        private void Info_ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loadingNewData && InfoTab_Class_ComboBox.SelectedIndex >= 0)
            {
                var selectedClassIndex = InfoTab_Class_ComboBox.SelectedIndex;
                var unrealIndex = selectedClassIndex - CurrentLoadedEntry.FileRef.ImportCount;
                if (unrealIndex == CurrentLoadedEntry?.UIndex)
                {
                    var exp = CurrentLoadedEntry as ExportEntry;
                    InfoTab_Class_ComboBox.SelectedIndex = exp.Class != null ? exp.Class.UIndex + CurrentLoadedEntry.FileRef.ImportCount : CurrentLoadedEntry.FileRef.ImportCount;
                    MessageBox.Show("Cannot set class to self, this will cause infinite recursion in game.");
                    return;
                }

                headerByteProvider.WriteBytes(HEADER_OFFSET_EXP_IDXCLASS, BitConverter.GetBytes(unrealIndex));
                Header_Hexbox.Refresh();
            }
        }

        private void InfoTab_Header_ByteProvider_InternalChanged(object sender, EventArgs e)
        {
            if (OriginalHeader != null)
            {
                HexChanged = !headerByteProvider.Span.SequenceEqual(OriginalHeader);
            }
        }

        private void Info_PackageLinkClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loadingNewData && InfoTab_PackageLink_ComboBox.SelectedIndex >= 0)
            {

                var selectedImpExp = InfoTab_PackageLink_ComboBox.SelectedIndex;
                var unrealIndex = selectedImpExp - CurrentLoadedEntry.FileRef.ImportCount; //get the actual UIndex
                if (unrealIndex == CurrentLoadedEntry?.UIndex)
                {
                    MessageBox.Show("Cannot link to self, this will cause infinite recursion.");
                    InfoTab_PackageLink_ComboBox.SelectedIndex = CurrentLoadedEntry.idxLink + CurrentLoadedEntry.FileRef.ImportCount;
                    return;
                }
                headerByteProvider.WriteBytes(CurrentLoadedEntry is ExportEntry ? HEADER_OFFSET_EXP_IDXLINK : HEADER_OFFSET_IMP_IDXLINK, BitConverter.GetBytes(unrealIndex));
                Header_Hexbox.Refresh();
            }
        }

        private void Info_SuperClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loadingNewData && InfoTab_Superclass_ComboBox.SelectedIndex >= 0)
            {
                var selectedClassIndex = InfoTab_Superclass_ComboBox.SelectedIndex;
                var unrealIndex = selectedClassIndex - CurrentLoadedEntry.FileRef.ImportCount;
                if (unrealIndex == CurrentLoadedEntry?.UIndex)
                {
                    MessageBox.Show("Cannot set superclass to self, this will cause infinite recursion in game.");
                    var exp = CurrentLoadedEntry as ExportEntry;

                    if (exp.HasSuperClass)
                    {
                        InfoTab_Superclass_ComboBox.SelectedIndex = exp.SuperClass.UIndex + CurrentLoadedEntry.FileRef.ImportCount;
                    }
                    else
                    {
                        InfoTab_Superclass_ComboBox.SelectedIndex = CurrentLoadedEntry.FileRef.ImportCount; //0
                    }
                    return;
                }

                headerByteProvider.WriteBytes(HEADER_OFFSET_EXP_IDXSUPERCLASS, BitConverter.GetBytes(unrealIndex));
                Header_Hexbox.Refresh();
            }
        }

        private void Info_ObjectNameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loadingNewData && InfoTab_Objectname_ComboBox.SelectedIndex >= 0 && CurrentLoadedEntry != null)
            {
                var selectedNameIndex = InfoTab_Objectname_ComboBox.SelectedIndex;
                if (selectedNameIndex >= 0)
                {
                    headerByteProvider.WriteBytes(CurrentLoadedEntry is ExportEntry ? HEADER_OFFSET_EXP_IDXOBJECTNAME : HEADER_OFFSET_IMP_IDXOBJECTNAME, BitConverter.GetBytes(selectedNameIndex));
                    Header_Hexbox.Refresh();
                }
            }
        }

        private void Info_IndexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loadingNewData)
            {
                if (int.TryParse(InfoTab_ObjectnameIndex_TextBox.Text, out int x))
                {
                    headerByteProvider.WriteBytes(CurrentLoadedEntry is ExportEntry ? HEADER_OFFSET_EXP_INDEXVALUE : HEADER_OFFSET_IMP_IDXOBJECTNAME + 4, BitConverter.GetBytes(x));
                    Header_Hexbox.Refresh();
                }
            }
        }

        private void Info_ArchetypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loadingNewData && InfoTab_Archetype_ComboBox.SelectedIndex >= 0)
            {
                var selectedArchetTypeIndex = InfoTab_Archetype_ComboBox.SelectedIndex;
                var unrealIndex = selectedArchetTypeIndex - CurrentLoadedEntry.FileRef.ImportCount;
                if (unrealIndex == CurrentLoadedEntry?.UIndex)
                {
                    MessageBox.Show("Cannot set archetype to self, this will cause infinite recursion in game.");
                    var exp = CurrentLoadedEntry as ExportEntry;

                    if (exp.HasArchetype)
                    {
                        InfoTab_Archetype_ComboBox.SelectedIndex = exp.Archetype.UIndex + CurrentLoadedEntry.FileRef.ImportCount;
                    }
                    else
                    {
                        InfoTab_Archetype_ComboBox.SelectedIndex = CurrentLoadedEntry.FileRef.ImportCount; //0
                    }
                    return;
                }

                headerByteProvider.WriteBytes(HEADER_OFFSET_EXP_IDXARCHETYPE, BitConverter.GetBytes(unrealIndex));
                Header_Hexbox.Refresh();
            }
        }

        private void Info_PackageFileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loadingNewData && InfoTab_PackageFile_ComboBox.SelectedIndex >= 0)
            {
                var selectedNameIndex = InfoTab_PackageFile_ComboBox.SelectedIndex;
                headerByteProvider.WriteBytes(HEADER_OFFSET_IMP_IDXPACKAGEFILE, BitConverter.GetBytes(selectedNameIndex));
                Header_Hexbox.Refresh();
            }
        }

        private void Info_ImpClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loadingNewData && InfoTab_ImpClass_ComboBox.SelectedIndex >= 0)
            {
                var selectedNameIndex = InfoTab_ImpClass_ComboBox.SelectedIndex;
                headerByteProvider.WriteBytes(HEADER_OFFSET_IMP_IDXCLASSNAME, BitConverter.GetBytes(selectedNameIndex));
                Header_Hexbox.Refresh();
            }
        }

        private void InfoTab_Objectname_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = CurrentLoadedEntry is ExportEntry ? HEADER_OFFSET_EXP_IDXOBJECTNAME : HEADER_OFFSET_IMP_IDXOBJECTNAME;
            Header_Hexbox.SelectionLength = 4;
        }

        private void InfoTab_Class_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = HEADER_OFFSET_EXP_IDXCLASS;
            Header_Hexbox.SelectionLength = 4;
        }

        private void InfoTab_ImpClass_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = HEADER_OFFSET_IMP_IDXCLASSNAME;
            Header_Hexbox.SelectionLength = 4;
        }

        private void InfoTab_Superclass_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = HEADER_OFFSET_EXP_IDXSUPERCLASS;
            Header_Hexbox.SelectionLength = 4;
        }

        private void InfoTab_PackageLink_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = CurrentLoadedEntry is ExportEntry ? HEADER_OFFSET_EXP_IDXLINK : HEADER_OFFSET_IMP_IDXLINK;
            Header_Hexbox.SelectionLength = 4;
        }

        private void InfoTab_PackageFile_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = HEADER_OFFSET_IMP_IDXPACKAGEFILE;
            Header_Hexbox.SelectionLength = 4;
        }

        private void InfoTab_Archetype_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = HEADER_OFFSET_EXP_IDXARCHETYPE;
            Header_Hexbox.SelectionLength = 4;
        }

        private void InfoTab_Flags_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = HEADER_OFFSET_EXP_OBJECTFLAGS;
            Header_Hexbox.SelectionLength = 8;
        }

        private void InfoTab_ObjectNameIndex_ComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Header_Hexbox.SelectionStart = CurrentLoadedEntry is ExportEntry ? HEADER_OFFSET_EXP_IDXOBJECTNAME + 4 : HEADER_OFFSET_IMP_IDXOBJECTNAME + 4;
            Header_Hexbox.SelectionLength = 4;
        }

        /// <summary>
        /// Handler for when the flags combobox item changes value
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InfoTab_Flags_ComboBox_ItemSelectionChanged(object sender, ItemSelectionChangedEventArgs e)
        {
            if (!loadingNewData)
            {
                EObjectFlags newFlags = 0U;
                foreach (var flag in InfoTab_Flags_ComboBox.Items)
                {
                    if (InfoTab_Flags_ComboBox.ItemContainerGenerator.ContainerFromItem(flag) is SelectorItem selectorItem && selectorItem.IsSelected == true)
                    {
                        newFlags |= (EObjectFlags)flag;
                    }
                }
                //Debug.WriteLine(newFlags);
                headerByteProvider.WriteBytes(HEADER_OFFSET_EXP_OBJECTFLAGS, BitConverter.GetBytes((ulong)newFlags));
                Header_Hexbox.Refresh();
            }
        }

        private void MetadataEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (!ControlLoaded)
            {
                Header_Hexbox = (HexBox)Header_Hexbox_Host.Child;
                headerByteProvider = new ReadOptimizedByteProvider();
                Header_Hexbox.ByteProvider = headerByteProvider;
                if (CurrentLoadedEntry != null) headerByteProvider.ReplaceBytes(CurrentLoadedEntry.Header);
                headerByteProvider.Changed += InfoTab_Header_ByteProvider_InternalChanged;
                ControlLoaded = true;

                Header_Hexbox.SelectionStartChanged -= hb1_SelectionChanged;
                Header_Hexbox.SelectionLengthChanged -= hb1_SelectionChanged;

                Header_Hexbox.SelectionStartChanged += hb1_SelectionChanged;
                Header_Hexbox.SelectionLengthChanged += hb1_SelectionChanged;
            }
        }

        /// <summary>
        /// Handles pressing the enter key when the class dropdown is active. Automatically will attempt to find the next object by class.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InfoTab_Objectname_ComboBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                //Check name
                var text = InfoTab_Objectname_ComboBox.Text;
                int index = CurrentLoadedEntry.FileRef.findName(text);
                if (index < 0 && !string.IsNullOrEmpty(text))
                {
                    Keyboard.ClearFocus();
                    string input = $"The name \"{text}\" does not exist in the current loaded package.\nIf you'd like to add this name, press enter below, or change the name to what you would like it to be.";
                    string result = PromptDialog.Prompt(this, input, "Enter new name", text);
                    if (!string.IsNullOrEmpty(result))
                    {
                        int idx = CurrentLoadedEntry.FileRef.FindNameOrAdd(result);
                        if (idx != CurrentLoadedEntry.FileRef.Names.Count - 1)
                        {
                            //not the last
                            MessageBox.Show($"{result} already exists in this package file.\nName index: {idx} (0x{idx:X8})", "Name already exists");
                        }
                        else
                        {
                            CurrentObjectNameIndex = idx;
                        }
                        //refresh should be triggered by hosting window
                    }
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        public override void SignalNamelistAboutToUpdate()
        {
            CurrentObjectNameIndex = CurrentObjectNameIndex >= 0 ? CurrentObjectNameIndex : InfoTab_Objectname_ComboBox.SelectedIndex;
        }

        public override void SignalNamelistChanged()
        {
            InfoTab_Objectname_ComboBox.SelectedIndex = CurrentObjectNameIndex;
            CurrentObjectNameIndex = -1;
        }

        public override void Dispose()
        {
            if (Header_Hexbox != null)
            {
                Header_Hexbox.SelectionStartChanged -= hb1_SelectionChanged;
                Header_Hexbox.SelectionLengthChanged -= hb1_SelectionChanged;
            }

            Header_Hexbox = null;
            Header_Hexbox_Host?.Child.Dispose();
            Header_Hexbox_Host?.Dispose();
            Header_Hexbox_Host = null;
            AllEntriesList.Clear();
        }

        /// <summary>
        /// This class is used when stuffing into the list. It makes "0" searchable by having the UIndex property.
        /// </summary>
        private class ZeroUIndexClassEntry
        {
            public static readonly ZeroUIndexClassEntry instance = new();

            private ZeroUIndexClassEntry() { }

            public override string ToString() => "0: Class";

            public int UIndex => 0;
        }
    }
}