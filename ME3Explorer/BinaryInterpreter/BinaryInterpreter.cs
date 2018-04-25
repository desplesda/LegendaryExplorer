﻿using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Be.Windows.Forms;
using ME3Explorer.Unreal;
using ME3Explorer.Unreal.Classes;
using ME3Explorer.Packages;
using KFreonLib.MEDirectories;
using System.Diagnostics;
using ME1Explorer.Unreal;
using ME2Explorer.Unreal;
using ME1Explorer.Unreal.Classes;
using System.Xml;
using System.Xml.Linq;
using Gibbed.IO;

namespace ME3Explorer
{
    public partial class BinaryInterpreter : UserControl
    {
        public IMEPackage Pcc { get { return pcc; } set { pcc = value; defaultStructValues.Clear(); } }

        public int InterpreterMode { get; private set; }
        private const int INTERPRETERMODE_OBJECTS = 0;
        private const int INTERPRETERMODE_NAMES = 1;
        private const int INTERPRETERMODE_INTEGERS = 2;
        private const int INTERPRETERMODE_FLOATS = 3;
        /*
         * Objects
Names
Integers
Floats*/
        public IExportEntry export;
        public string className;
        public byte[] memory;
        public int memsize;
        //public int readerpos;

        public struct PropHeader
        {
            public int name;
            public int type;
            public int size;
            public int index;
            public int offset;
        }

        public string[] Types =
        {
            "StructProperty", //0
            "IntProperty",
            "FloatProperty",
            "ObjectProperty",
            "NameProperty",
            "BoolProperty",  //5
            "ByteProperty",
            "ArrayProperty",
            "StrProperty",
            "StringRefProperty",
            "DelegateProperty",//10
            "None",
            "BioMask4Property",
        };

        public enum nodeType
        {
            Unknown = -1,
            StructProperty = 0,
            IntProperty = 1,
            FloatProperty = 2,
            ObjectProperty = 3,
            NameProperty = 4,
            BoolProperty = 5,
            ByteProperty = 6,
            ArrayProperty = 7,
            StrProperty = 8,
            StringRefProperty = 9,
            DelegateProperty = 10,
            None,
            BioMask4Property,

            ArrayLeafObject,
            ArrayLeafName,
            ArrayLeafEnum,
            ArrayLeafStruct,
            ArrayLeafBool,
            ArrayLeafString,
            ArrayLeafFloat,
            ArrayLeafInt,
            ArrayLeafByte,

            StructLeafByte,
            StructLeafFloat,
            StructLeafDeg, //indicates this is a StructProperty leaf that is in degrees (actually unreal rotation units)
            StructLeafInt,
            StructLeafObject,
            StructLeafName,
            StructLeafBool,
            StructLeafStr,
            StructLeafArray,
            StructLeafEnum,
            StructLeafStruct,

            Root,
        }

        Dictionary<int, string> me1TLK = new Dictionary<int, string>();
        private int lastSetOffset = -1; //offset set by program, used for checking if user changed since set 
        private nodeType LAST_SELECTED_PROP_TYPE = nodeType.Unknown; //last property type user selected. Will use to check the current offset for type
        private TreeNode LAST_SELECTED_NODE = null; //last selected tree node
        public int HEXBOX_MAX_WIDTH = 650;

        private IMEPackage pcc;
        private Dictionary<string, List<PropertyReader.Property>> defaultStructValues;

        int? selectedNodePos = null;
        private Dictionary<string, string> ME1_TLK_DICT; //TODO: Read TLK for ME1 for Bio2DA
        public static readonly string[] ParsableBinaryClasses = { "Level", "StaticMeshCollectionActor", "Class", "BioStage", "ObjectProperty", "Const",
               "Enum", "ArrayProperty","FloatProperty", "IntProperty", "BoolProperty","Enum","ObjectRedirector", "WwiseEvent", "Material", "StaticMesh", "MaterialInstanceConstant",
            "BioDynamicAnimSet", "StaticMeshComponent", "SkeletalMeshComponent", "SkeletalMesh", "Model", "Polys" }; //classes that have binary parse code or shoudl show up in generic scan


        public BinaryInterpreter()
        {

            InitializeComponent();
            SetTopLevel(false);
            defaultStructValues = new Dictionary<string, List<PropertyReader.Property>>();

            //Load ME1TLK
            /*string tlkxmlpath = @"C:\users\mgame\desktop\me1tlk.xml";
            if (File.Exists(tlkxmlpath))
            {
                XDocument xmlDocument = XDocument.Load(tlkxmlpath);
                ME1_TLK_DICT =
                    (from strings in xmlDocument.Descendants("string")
                     select new
                     {
                         ID = strings.Element("id").Value,
                         Data = strings.Element("data").Value,
                     }).Distinct().ToDictionary(o => o.ID, o => o.Data);
            }*/
        }

        /// <summary>
        /// DON'T USE THIS FOR NOW! Used for relinking.
        /// </summary>
        /// <param name="export">Export to scan.</param>
        public BinaryInterpreter(IMEPackage importingPCC, IExportEntry importingExport, IMEPackage destPCC, IExportEntry destExport, SortedDictionary<int, int> crossPCCReferences)
        {
            //This will make it fairly slow, but will make it so I don't have to change everything.
            InitializeComponent();


            SetTopLevel(false);
            defaultStructValues = new Dictionary<string, List<PropertyReader.Property>>();
            this.pcc = importingPCC;
            this.export = importingExport;
            memory = export.Data;
            memsize = memory.Length;
            className = export.ClassName;
            //StartScan();
            RelinkObjectProperties(crossPCCReferences, treeView1.SelectedNode, destExport);
        }

        private void RelinkObjectProperties(SortedDictionary<int, int> crossPCCReferences, TreeNode rootNode, IExportEntry destinationExport)
        {
            if (rootNode != null)
            {
                if (rootNode.Nodes.Count > 0)
                {
                    //container.
                    foreach (TreeNode node in rootNode.Nodes)
                    {
                        RelinkObjectProperties(crossPCCReferences, node, destinationExport);
                    }
                }
                else
                {
                    //leaf
                    if (rootNode.Tag != null)
                    {
                        if ((nodeType)rootNode.Tag == nodeType.ObjectProperty || (nodeType)rootNode.Tag == nodeType.StructLeafObject || (nodeType)rootNode.Tag == nodeType.ArrayLeafObject)
                        {

                            int valueoffset = 0;
                            if ((nodeType)rootNode.Tag == nodeType.ObjectProperty)
                            {
                                valueoffset = 24;
                            }

                            int off = getPosFromNode(rootNode) + valueoffset;
                            int n = BitConverter.ToInt32(memory, off);
                            if (n > 0)
                            {
                                n--;
                            }
                            //if (n < -1)
                            //{
                            //    n++;
                            //}
                            //Debug.WriteLine(rootNode.Tag + " " + n + " " + rootNode.Text);
                            if (n != 0)
                            {
                                int key;
                                if (crossPCCReferences.TryGetValue(n, out key))
                                {
                                    byte[] data = destinationExport.Data;
                                    //we can remap this
                                    if (key > 0)
                                    {
                                        key++; //+1 indexing
                                    }
                                    byte[] buff2 = BitConverter.GetBytes(key);
                                    for (int o = 0; o < 4; o++)
                                    {
                                        //Write object property value
                                        //byte preval = exportdata[o + o];
                                        data[off + o] = buff2[o];
                                        //byte postval = exportdata[destprop.offsetval + o];

                                        //Debug.WriteLine("Updating Byte at 0x" + (destprop.offsetval + o).ToString("X4") + " from " + preval + " to " + postval + ". It should have been set to " + buff2[o]);
                                    }
                                    destinationExport.Data = data;
                                }
                                else
                                {
                                    Debug.WriteLine("Relink miss: " + n + " " + rootNode.Text);
                                }
                            }



                            //if (n > 0)
                            //{
                            //    //update export
                            //    Debug.WriteLine("EX Object Data: " + n + " " + pcc.Exports[n - 1].ObjectName);
                            //}
                            //else if (n < 0)
                            //{
                            //    //update import ref
                            //    Debug.WriteLine("IM Object Data: " + n + " " + pcc.Imports[-n - 1].ObjectName);

                            //}
                        }
                    }
                }
            }
        }

        public void InitInterpreter()
        {
            memory = export.Data;
            memsize = memory.Length;
            DynamicByteProvider db = new DynamicByteProvider(export.Data);
            hb1.ByteProvider = db;
            className = export.ClassName;
            StartScan();
        }

        private void StartScan(string topNodeName = null, string selectedNodeName = null)
        {
            viewModeDropDownList.Visible = false;
            switch (className)
            {
                case "Class":
                    StartClassScan2();
                    break;
                case "Enum":
                case "Const":
                    StartEnumScan();
                    break;
                case "IntProperty":
                case "BoolProperty":
                case "ArrayProperty":
                case "FloatProperty":
                case "ObjectProperty":
                    StartObjectScan();
                    break;
                case "Level":
                    StartLevelScan();
                    break;
                case "StaticMeshCollectionActor":
                    StartStaticMeshCollectionActorScan();
                    break;
                case "Material":
                    StartMaterialScan();
                    break;
                case "BioDynamicAnimSet":
                    StartBioDynamicAnimSetScan();
                    break;
                case "WwiseEvent":
                    StartWWiseEventScan();
                    break;
                case "Bio2DA":
                case "Bio2DANumberedRows":
                    StartBio2DAScan();
                    break;
                case "ObjectRedirector":
                    StartObjectRedirectorScan();
                    break;
                case "BioStage":
                    StartBioStageScan();
                    break;
                default:
                    StartGenericScan();
                    break;
            }

            var nodes = treeView1.Nodes.Find(topNodeName, true);
            if (nodes.Length > 0)
            {
                treeView1.TopNode = nodes[0];
            }

            nodes = treeView1.Nodes.Find(selectedNodeName, true);
            if (nodes.Length > 0)
            {
                if (treeView1.Nodes.Count > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
            }
            else
            {
                if (treeView1.Nodes.Count > 0)
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            if (treeView1.SelectedNode != null)
            {
                treeView1.SelectedNode.Expand();
            }

        }

        private void StartEnumScan(string nodeNameToSelect = null)
        {
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName + "(" + export.ClassName + ")");
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                TreeNode node;

                byte[] data = export.Data;
                int offset = 0;
                int unrealExportIndex = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unreal Unique Index: " + unrealExportIndex);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                int noneUnrealProperty = BitConverter.ToInt32(data, offset);
                int noneUnrealPropertyIndex = BitConverter.ToInt32(data, offset + 4);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unreal property None Name: " + pcc.getNameEntry(noneUnrealProperty));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafName;
                topLevelTree.Nodes.Add(node);
                offset += 8;

                int superclassIndex = BitConverter.ToInt32(data, offset);
                string superclassStr = getEntryFullPath(superclassIndex);

                node = new TreeNode("0x" + offset.ToString("X5") + " Superclass: " + superclassIndex + "(" + superclassStr + ")");
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafObject;

                topLevelTree.Nodes.Add(node);
                offset += 4;

                int classObjTree = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " NextItemCompilingChain: " + classObjTree + " " + getEntryFullPath(classObjTree));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafObject;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                if (export.ClassName == "Enum")
                {

                    int enumSize = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " Enum Size: " + enumSize);
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafInt;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;

                    for (int i = 0; i < enumSize; i++)
                    {
                        int enumName = BitConverter.ToInt32(data, offset);
                        int enumNameIndex = BitConverter.ToInt32(data, offset + 4);
                        node = new TreeNode("0x" + offset.ToString("X5") + " EnumName[" + i + "]: " + pcc.getNameEntry(enumName));
                        node.Name = offset.ToString();
                        node.Tag = nodeType.StructLeafName;
                        topLevelTree.Nodes.Add(node);
                        offset += 8;
                    }
                }

                if (export.ClassName == "Const")
                {
                    int literalStringLength = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " Const Literal Length: " + literalStringLength);
                    node.Name = offset.ToString();
                    node.Tag = nodeType.IntProperty;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;

                    //value is stored as a literal string in binary.
                    MemoryStream stream = new MemoryStream(data);
                    stream.Position = offset;
                    if (literalStringLength < 0)
                    {
                        string str = stream.ReadString((literalStringLength * -2), true, Encoding.Unicode);
                        node = new TreeNode("0x" + offset.ToString("X5") + " Const Literal Value: " + str);
                        node.Name = offset.ToString();
                        node.Tag = nodeType.StrProperty;
                        topLevelTree.Nodes.Add(node);
                    }
                }
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the " + export.ClassName + " binary: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;

            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartObjectScan(string nodeNameToSelect = null)
        {
            //const int nonTableEntryCount = 2; //how many items we parse that are not part of the functions table. e.g. the count, the defaults pointer
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName + "(" + export.ClassName + ")");
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                TreeNode node;

                byte[] data = export.Data;
                int offset = 0;
                int unrealExportIndex = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unreal Unique Index: " + unrealExportIndex);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                int noneUnrealProperty = BitConverter.ToInt32(data, offset);
                int noneUnrealPropertyIndex = BitConverter.ToInt32(data, offset + 4);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unreal property None Name: " + pcc.getNameEntry(noneUnrealProperty));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafName;
                topLevelTree.Nodes.Add(node);
                offset += 8;

                int superclassIndex = BitConverter.ToInt32(data, offset);
                string superclassStr = getEntryFullPath(superclassIndex);

                node = new TreeNode("0x" + offset.ToString("X5") + " Superclass: " + superclassIndex + "(" + superclassStr + ")");
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafObject;

                topLevelTree.Nodes.Add(node);
                offset += 4;

                int classObjTree = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " NextItemCompilingChain: " + classObjTree + " " + getEntryFullPath(classObjTree));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafObject;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                UInt64 ObjectFlagsMask = BitConverter.ToUInt64(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " ObjectFlags: 0x" + ObjectFlagsMask.ToString("X16"));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);

                //Create objectflags tree
                //This is such a hack job but I can't figure out how to do enums :(
                foreach (string row in UnrealFlags.propertyflags)
                {
                    string[] t = row.Split(',');
                    ulong l = ulong.Parse(t[1].Trim(), System.Globalization.NumberStyles.HexNumber);
                    if ((l & ObjectFlagsMask) != 0)
                    {
                        string reason = t.Length == 3 ? t[2] : "";
                        TreeNode flagnode = new TreeNode(t[0] + " " + t[1]+ " " +reason);
                        flagnode.Name = offset.ToString();
                        node.Nodes.Add(flagnode);
                    }
                }
                offset += 8;

                int unk1 = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unknown1 " + unk1);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                //has listed outerclass
                int none = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " None: " + pcc.getNameEntry(none));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 8;

                int unk2 = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unknown2: " + unk2);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4; //

                if (export.ClassName == "ObjectProperty")
                {
                    //has listed outerclass
                    int outer = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " OuterClass: " + outer + " " + getEntryFullPath(outer));
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafInt;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;
                }
                else if (export.ClassName == "ArrayProperty")
                {
                    //has listed outerclass
                    int outer = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " Array can hold objects of type: " + outer + " " + getEntryFullPath(outer));
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafInt;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;
                }
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the " + export.ClassName + " binary: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;

            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartBioStageScan(string nodeNameToSelect = null)
        {
            /*
             * Length (int)
                Name: m_aCameraList
                int unknown 0
                Count + int unknown
                [Camera name
                property name + floatproperty + length +float value (repeated like:
                fPitchDelta name + floatproperty + length +float value
                fYawDelta name + floatproperty + length +float value)
                None]*/
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            if ((export.header[0x1f] & 0x2) != 0)
            {
                byte[] data = export.Data;
                TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
                treeView1.Nodes.Add(topLevelTree);
                treeView1.CollapseAll();

                int binstartoffset = findEndOfProps();
                int pos = binstartoffset;
                int length = BitConverter.ToInt32(data, binstartoffset);
                TreeNode node = new TreeNode(binstartoffset.ToString("X4") + " Length: " + length);
                node.Name = binstartoffset.ToString();
                topLevelTree.Nodes.Add(node);
                pos += 4;

                int nameindex = BitConverter.ToInt32(data, pos);
                int nameindexunreal = BitConverter.ToInt32(data, pos + 4);

                string name = pcc.getNameEntry(nameindex);
                node = new TreeNode(pos.ToString("X4") + " Camera: " + name + "_" + nameindexunreal);
                node.Name = pos.ToString();
                node.Tag = nodeType.StructLeafName;
                topLevelTree.Nodes.Add(node);

                pos += 8;
                int shouldbezero = BitConverter.ToInt32(data, pos);
                if (shouldbezero != 0)
                {
                    Debug.WriteLine("NOT ZERO FOUND: " + pos);
                }
                pos += 4;

                int count = BitConverter.ToInt32(data, pos);
                node = new TreeNode(pos.ToString("X4") + " Count: " + count);
                node.Name = pos.ToString();
                topLevelTree.Nodes.Add(node);
                pos += 4;

                shouldbezero = BitConverter.ToInt32(data, pos);
                if (shouldbezero != 0)
                {
                    Debug.WriteLine("NOT ZERO FOUND: " + pos);
                }
                pos += 4;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        nameindex = BitConverter.ToInt32(data, pos);
                        nameindexunreal = BitConverter.ToInt32(data, pos + 4);
                        TreeNode parentnode = new TreeNode(pos.ToString("X4") + " Camera " + (i + 1) + ": " + pcc.getNameEntry(nameindex) + "_" + nameindexunreal);
                        topLevelTree.Nodes.Add(parentnode);
                        parentnode.Tag = nodeType.StructLeafName;
                        parentnode.Name = pos.ToString();
                        pos += 8;

                        while (pos < data.Length)
                        {
                            nameindex = BitConverter.ToInt32(data, pos);
                            nameindexunreal = BitConverter.ToInt32(data, pos + 4);
                            if (pcc.getNameEntry(nameindex) == "None")
                            {
                                node = new TreeNode(pos.ToString("X4") + " None");
                                parentnode.Nodes.Add(node);
                                node.Tag = nodeType.None;
                                node.Name = pos.ToString();
                                pos += 8;
                                break;
                            }

                            //propertyname
                            TreeNode propertyNode = new TreeNode(pos.ToString("X4") + " PropertyName: " + pcc.getNameEntry(nameindex));
                            propertyNode.Tag = nodeType.StructLeafName;
                            propertyNode.Name = pos.ToString();
                            parentnode.Nodes.Add(propertyNode);
                            pos += 8;

                            //FloatProperty
                            nameindex = BitConverter.ToInt32(data, pos);
                            nameindexunreal = BitConverter.ToInt32(data, pos + 4);
                            if (pcc.getNameEntry(nameindex) != "FloatProperty")
                            {
                                Debug.WriteLine("NOT FLOATPROPERTY");
                            }
                            pos += 8;

                            long len = BitConverter.ToInt64(data, pos);
                            pos += 8;

                            float value = BitConverter.ToSingle(data, pos);
                            TreeNode valueNode = new TreeNode(pos.ToString("X4") + " Value: " + value);
                            valueNode.Tag = nodeType.StructLeafFloat;
                            valueNode.Name = pos.ToString();
                            propertyNode.Nodes.Add(valueNode);
                            pos += (int)len;
                            #region debugway
                            /*nameindex = BitConverter.ToInt32(data, pos);
                            nameindexunreal = BitConverter.ToInt32(data, pos + 4);
                            if (pcc.getNameEntry(nameindex) == "None")
                            {
                                node = new TreeNode(pos.ToString("X4") + " None");
                                parentnode.Nodes.Add(node);
                                node.Tag = nodeType.None;
                                node.Name = pos.ToString();
                                pos += 8;
                                break;
                            }

                            //propertyname
                            node = new TreeNode(pos.ToString("X4") + " PropertyName: " + pcc.getNameEntry(nameindex));
                            node.Tag = nodeType.StructLeafName;
                            node.Name = pos.ToString();
                            parentnode.Nodes.Add(node);
                            pos += 8;

                            //FloatProperty
                            nameindex = BitConverter.ToInt32(data, pos);
                            nameindexunreal = BitConverter.ToInt32(data, pos + 4);
                            if (pcc.getNameEntry(nameindex) != "FloatProperty")
                            {
                                Debug.WriteLine("NOT FLOATPROPERTY");
                            }
                            node = new TreeNode(pos.ToString("X4") + " FloatProperty: " + pcc.getNameEntry(nameindex));
                            node.Tag = nodeType.StructLeafName;
                            node.Name = pos.ToString();
                            parentnode.Nodes.Add(node);
                            pos += 8;

                            long len = BitConverter.ToInt64(data, pos);
                            node = new TreeNode(pos.ToString("X4") + " Length: " + len);
                            node.Tag = nodeType.StructLeafInt;
                            node.Name = pos.ToString();
                            parentnode.Nodes.Add(node);
                            pos += 8;

                            float value = BitConverter.ToSingle(data, pos);
                            node = new TreeNode(pos.ToString("X4") + " Value: " + value);
                            node.Tag = nodeType.StructLeafInt;
                            node.Name = pos.ToString();
                            parentnode.Nodes.Add(node);
                            pos += 4;*/
                            #endregion
                        }
                    }
                }
                catch (Exception ex)
                {
                    topLevelTree.Nodes.Add(new TreeNode("Error reading binary data: " + ex.ToString()));
                }
                topLevelTree.Expand();
                treeView1.Nodes[0].Expand();
                TreeNode[] nodes;
                if (nodeNameToSelect != null)
                {
                    nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                    if (nodes.Length > 0)
                    {
                        treeView1.SelectedNode = nodes[0];
                    }
                    else
                    {
                        treeView1.SelectedNode = treeView1.Nodes[0];
                    }
                }

                //find start of class binary (end of props). This should 
                topLevelTree.Tag = nodeType.Root;
                topLevelTree.Name = "0";
            }
            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartObjectRedirectorScan(string nodeNameToSelect = null)
        {
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            byte[] data = export.Data;
            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();

            int binstartoffset = findEndOfProps();
            int redirnum = BitConverter.ToInt32(data, binstartoffset);
            TreeNode node = new TreeNode(binstartoffset.ToString("X4") + " Redirect references to this export to: " + redirnum + " " + pcc.getEntry(redirnum).GetFullPath);
            node.Name = binstartoffset.ToString();
            topLevelTree.Nodes.Add(node);
            topLevelTree.Expand();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            //find start of class binary (end of props). This should 
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartBio2DAScan(string nodeNameToSelect = null)
        {
            Random random = new Random();
            string[] stringRefColumns = { "StringRef", "SaveGameStringRef", "Title", "LabelRef", "Name", "ActiveWorld", "Description", "Description1", "Description1", "Description1", "ButtonLabel", "UnlockName", "UnlockBlurb", "DisplayName", "DisplayDescription", "PriAbiDesc", "SecAbiDesc" };

            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            byte[] data = export.Data;

            List<string> rowNames = new List<string>();
            if (export.ClassName == "Bio2DA")
            {
                string rowLabelsVar = "m_sRowLabel";
                var props = export.GetProperty<ArrayProperty<NameProperty>>(rowLabelsVar);
                if (props != null)
                {
                    foreach (NameProperty n in props)
                    {
                        rowNames.Add(n.ToString());
                    }
                }
                else
                {
                    return;
                }
            }
            else
            {
                string rowLabelsVar = "m_lstRowNumbers"; //Bio2DANumberedRows
                var props = export.GetProperty<ArrayProperty<IntProperty>>(rowLabelsVar);
                if (props != null)
                {
                    foreach (IntProperty n in props)
                    {
                        rowNames.Add(n.Value.ToString());
                    }
                }
                else
                {
                    return;
                }
            }

            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();

            //Get Columns
            List<string> columnNames = new List<string>();
            int colcount = BitConverter.ToInt32(data, data.Length - 4); //this is actually index of last column, but it works the same
            int currentcoloffset = 0;
            Console.WriteLine("Number of columns: " + colcount);
            TreeNode columnsnode = new TreeNode("Columns");

            while (colcount >= 0)
            {
                currentcoloffset += 4;
                int colindex = BitConverter.ToInt32(data, data.Length - currentcoloffset);
                currentcoloffset += 8; //names in this case don't use nameindex values.
                int nameindex = BitConverter.ToInt32(data, data.Length - currentcoloffset);
                string name = pcc.getNameEntry(nameindex);
                //Console.WriteLine(name + " at col pos " + colindex);
                TreeNode column = new TreeNode(colindex + ": " + name);
                column.Name = (data.Length - currentcoloffset).ToString();
                columnsnode.Nodes.Insert(0, column);
                columnNames.Insert(0, name);
                colcount--;
            }
            currentcoloffset += 4;  //real column count
            int infilecolcount = BitConverter.ToInt32(data, data.Length - currentcoloffset);
            columnsnode.Text = infilecolcount + " columns";
            columnsnode.Name = (data.Length - currentcoloffset).ToString();

            //start of binary data
            int binstartoffset = findEndOfProps(); //arrayheader + nonenamesize + number of items in this list
            int curroffset = binstartoffset;

            int cellcount = BitConverter.ToInt32(data, curroffset);
            if (cellcount > 0)
            {

                TreeNode node = new TreeNode(curroffset.ToString("X4") + " Number of cells in this Bio2DA : " + cellcount);
                node.Name = curroffset.ToString();
                topLevelTree.Nodes.Add(node);
                curroffset += 4;

                for (int i = 0; i < rowNames.Count(); i++)
                {
                    TreeNode rownode = new TreeNode(curroffset.ToString("X4") + ": " + rowNames[i]);
                    rownode.Name = curroffset.ToString();
                    topLevelTree.Nodes.Add(rownode);
                    for (int colindex = 0; colindex < columnNames.Count() && curroffset < data.Length - currentcoloffset; colindex++)
                    {
                        byte dataType = 255;
                        //if (cellcount != 0)
                        //{
                        dataType = data[curroffset];
                        curroffset++;
                        //}
                        string valueStr = "";
                        string nodename = curroffset.ToString();
                        string offsetstr = curroffset.ToString("X4");
                        nodeType tag = nodeType.Unknown;
                        switch (dataType)
                        {

                            case 0:
                                //int
                                int ival = BitConverter.ToInt32(data, curroffset);
                                valueStr = ival.ToString();
                                //if (stringRefColumns.Contains(columnNames[colindex]))
                                {
                                    string tlkVal;
                                    if (ME1_TLK_DICT != null && ME1_TLK_DICT.TryGetValue(valueStr, out tlkVal))
                                    {
                                        valueStr += " " + tlkVal;
                                    }
                                }
                                curroffset += 4;
                                tag = nodeType.StructLeafInt;
                                break;
                            case 1:
                                //name
                                int nval = BitConverter.ToInt32(data, curroffset);
                                valueStr = pcc.getNameEntry(nval);
                                valueStr += "_" + BitConverter.ToInt32(data, curroffset + 4);
                                curroffset += 8;
                                tag = nodeType.StructLeafName;
                                break;
                            case 2:
                                //float
                                float fval = BitConverter.ToSingle(data, curroffset);
                                valueStr = fval.ToString();
                                curroffset += 4;
                                tag = nodeType.StructLeafFloat;
                                break;
                        }

                        node = new TreeNode(offsetstr + " " + columnNames[colindex] + ": " + valueStr);
                        node.Name = nodename;
                        node.Tag = tag;
                        rownode.Nodes.Add(node);
                    }

                    //int loopstartoffset = curroffset;

                    //node = new TreeNode(curroffset.ToString("X4") + ": " + "(1b: " + data[curroffset] + " int: " + val + ")");
                    //node.Name = (curroffset).ToString();
                    //curroffset += 1;
                    //node.Name = loopstartoffset.ToString();
                    //topLevelTree.Nodes.Add(node);
                }
            }
            else
            {
                curroffset += 4; //theres a 0 here for some reason
                cellcount = BitConverter.ToInt32(data, curroffset);
                TreeNode node = new TreeNode(curroffset.ToString("X4") + " Number of indexed cells in this Bio2DA: " + cellcount);
                node.Name = curroffset.ToString();
                topLevelTree.Nodes.Add(node);
                curroffset += 4; //theres a 0 here for some reason

                Bio2DACell[,] bio2da = new Bio2DACell[rowNames.Count(), columnNames.Count()];
                //curroffset += 4;
                int numindexed = 0;
                while (numindexed < cellcount)
                {
                    int index = BitConverter.ToInt32(data, curroffset);
                    int row = index / columnNames.Count();
                    int col = index % columnNames.Count();
                    curroffset += 4;
                    byte dataType = data[curroffset];
                    int dataSize = dataType == Bio2DACell.TYPE_NAME ? 8 : 4;
                    curroffset++;
                    byte[] celldata = new byte[dataSize];
                    Buffer.BlockCopy(data, curroffset, celldata, 0, dataSize);
                    Bio2DACell cell = new Bio2DACell(pcc, curroffset, dataType, celldata);
                    //Console.WriteLine(columnNames[col] + ": " + cell.GetDisplayableValue());
                    bio2da[row, col] = cell;
                    numindexed++;
                    curroffset += dataSize;
                }

                for (int row = 0; row < bio2da.GetLength(0); row++)
                {
                    TreeNode rownode = new TreeNode(rowNames[row]);
                    rownode.Name = curroffset.ToString();
                    topLevelTree.Nodes.Add(rownode);
                    for (int col = 0; col < bio2da.GetLength(1); col++)
                    {
                        Bio2DACell cell = bio2da[row, col];
                        string columnname = columnNames[col];
                        TreeNode columnNode;
                        if (cell != null)
                        {
                            columnNode = new TreeNode(columnname + ": " + cell.GetDisplayableValue());
                            if (cell.Type == Bio2DACell.TYPE_INT)
                            {
                                string tlkVal = " ";
                                if (ME1_TLK_DICT != null && ME1_TLK_DICT.TryGetValue(BitConverter.ToInt32(cell.Data, 0).ToString(), out tlkVal))
                                {
                                    tlkVal = tlkVal.Replace("\n", "[NL]");
                                    columnNode.Text += " " + tlkVal;
                                }
                            }
                            switch (cell.Type)
                            {
                                case Bio2DACell.TYPE_FLOAT:
                                    columnNode.Tag = nodeType.StructLeafFloat;
                                    break;
                                case Bio2DACell.TYPE_NAME:
                                    columnNode.Tag = nodeType.StructLeafName;
                                    break;
                                case Bio2DACell.TYPE_INT:
                                    columnNode.Tag = nodeType.StructLeafInt;
                                    break;
                            }
                            columnNode.Name = cell.Offset.ToString();
                        }
                        else
                        {
                            columnNode = new TreeNode(columnname + ": Skipped by table");
                        }
                        rownode.Nodes.Add(columnNode);
                    }
                }
                TreeNode nodex = new TreeNode("Number of nodes indexed: " + numindexed);
                treeView1.Nodes.Add(nodex);
            }

            treeView1.Nodes.Add(columnsnode);
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            //find start of class binary (end of props). This should 
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";

            treeView1.EndUpdate();
            memory = data;
            //export.Data = data;
            memsize = memory.Length;
        }

        static float NextFloat(Random random)
        {
            double mantissa = (random.NextDouble() * 2.0) - 1.0;
            double exponent = Math.Pow(2.0, random.Next(-3, 20));
            return (float)(mantissa * exponent);
        }

        private void StartWWiseEventScan(string nodeNameToSelect = null)
        {
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            byte[] data = export.Data;

            int binarystart = findEndOfProps();
            //find start of class binary (end of props). This should 
            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                int binarypos = binarystart;
                List<TreeNode> subnodes = new List<TreeNode>();
                int count = BitConverter.ToInt32(data, binarypos);
                TreeNode node = new TreeNode("0x" + binarypos.ToString("X4") + " Count: " + count.ToString());
                subnodes.Add(node);
                binarypos += 4; //+ int
                if (count > 0)
                {
                    string nodeText = "0x" + binarypos.ToString("X4") + " ";
                    int val = BitConverter.ToInt32(data, binarypos);
                    string name = val.ToString();
                    if (val > 0 && val <= pcc.Exports.Count)
                    {
                        IExportEntry exp = pcc.Exports[val - 1];
                        nodeText += name + " " + exp.PackageFullName + "." + exp.ObjectName + " (" + exp.ClassName + ")";
                    }
                    else if (val < 0 && val != int.MinValue && Math.Abs(val) <= pcc.Imports.Count)
                    {
                        int csImportVal = Math.Abs(val) - 1;
                        ImportEntry imp = pcc.Imports[csImportVal];
                        nodeText += name + " " + imp.PackageFullName + "." + imp.ObjectName + " (" + imp.ClassName + ")";
                    }

                    node = new TreeNode(nodeText);
                    node.Tag = nodeType.StructLeafObject;
                    node.Name = binarypos.ToString();
                    subnodes.Add(node);
                    /*

                                        int objectindex = BitConverter.ToInt32(data, binarypos);
                                        IEntry obj = pcc.getEntry(objectindex);
                                        string nodeValue = obj.GetFullPath;
                                        node.Tag = nodeType.StructLeafObject;
                                        */
                }
                topLevelTree.Nodes.AddRange(subnodes.ToArray());
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the wwiseevent: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartBioDynamicAnimSetScan(string nodeNameToSelect = null)
        {
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            byte[] data = export.Data;

            int binarystart = findEndOfProps();
            //find start of class binary (end of props). This should 



            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                int binarypos = binarystart;
                List<TreeNode> subnodes = new List<TreeNode>();
                int count = BitConverter.ToInt32(data, binarypos);
                TreeNode node = new TreeNode("0x" + binarypos.ToString("X4") + " Count: " + count.ToString());
                subnodes.Add(node);
                binarypos += 4; //+ int
                for (int i = 0; i < count; i++)
                {
                    int nameIndex = BitConverter.ToInt32(data, binarypos);
                    int nameIndexNum = BitConverter.ToInt32(data, binarypos + 4);
                    int shouldBe1 = BitConverter.ToInt32(data, binarypos + 8);
                    string nodeValue = pcc.Names[nameIndex] + "_" + nameIndexNum;
                    if (shouldBe1 != 1)
                    {
                        //ERROR
                        nodeValue += " - Not followed by 1 (integer)!";
                    }

                    node = new TreeNode("0x" + binarypos.ToString("X4") + " Name: " + nodeValue);
                    node.Tag = nodeType.StructLeafName;
                    node.Name = binarypos.ToString();
                    subnodes.Add(node);
                    binarypos += 12;
                }
                topLevelTree.Nodes.AddRange(subnodes.ToArray());
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the biodynamicanimset: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartGenericScan(string nodeNameToSelect = null)
        {
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            byte[] data = export.Data;

            int binarystart = findEndOfProps();
            //find start of class binary (end of props). This should 
            viewModeDropDownList.Visible = true;
            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName + " (Generic Scan)");
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                int binarypos = binarystart;
                List<TreeNode> subnodes = new List<TreeNode>();

                //binarypos += 0x1C; //Skip ??? and GUID
                //int guid = BitConverter.ToInt32(data, binarypos);
                /*int num1 = BitConverter.ToInt32(data, binarypos);
                TreeNode node = new TreeNode("0x" + binarypos.ToString("X4") + " ???: " + num1.ToString());
                subnodes.Add(node);
                binarypos += 4;
                int num2 = BitConverter.ToInt32(data, binarypos);
                node = new TreeNode("0x" + binarypos.ToString("X4") + " Count: " + num2.ToString());
                subnodes.Add(node);
                binarypos += 4;
                */
                int datasize = 4;
                if (InterpreterMode == INTERPRETERMODE_NAMES)
                {
                    datasize = 8;
                }

                while (binarypos <= data.Length - datasize)
                {

                    string nodeText = "0x" + binarypos.ToString("X4") + " ";
                    TreeNode node = new TreeNode();

                    switch (InterpreterMode)
                    {
                        case INTERPRETERMODE_OBJECTS:
                            {
                                int val = BitConverter.ToInt32(data, binarypos);
                                string name = val.ToString();
                                if (val > 0 && val <= pcc.Exports.Count)
                                {
                                    IExportEntry exp = pcc.Exports[val - 1];
                                    nodeText += name + " " + exp.PackageFullName + "." + exp.ObjectName + " (" + exp.ClassName + ")";
                                }
                                else if (val < 0 && val != int.MinValue && Math.Abs(val) <= pcc.Imports.Count)
                                {
                                    int csImportVal = Math.Abs(val) - 1;
                                    ImportEntry imp = pcc.Imports[csImportVal];
                                    nodeText += name + " " + imp.PackageFullName + "." + imp.ObjectName + " (" + imp.ClassName + ")";
                                }
                                node.Tag = nodeType.StructLeafObject;
                                break;
                            }
                        case INTERPRETERMODE_NAMES:
                            {
                                int val = BitConverter.ToInt32(data, binarypos);
                                if (val > 0 && val <= pcc.Names.Count)
                                {
                                    IExportEntry exp = pcc.Exports[val - 1];
                                    nodeText += val + " \t" + pcc.getNameEntry(val);
                                }
                                else
                                {
                                    nodeText += "\t" + val;
                                }
                                node.Tag = nodeType.StructLeafName;
                                break;
                            }
                        case INTERPRETERMODE_FLOATS:
                            {
                                float val = BitConverter.ToSingle(data, binarypos);
                                nodeText += val.ToString();
                                node.Tag = nodeType.StructLeafFloat;
                                break;
                            }
                        case INTERPRETERMODE_INTEGERS:
                            {
                                int val = BitConverter.ToInt32(data, binarypos);
                                nodeText += val.ToString();
                                node.Tag = nodeType.StructLeafInt;
                                break;
                            }
                    }
                    node.Text = nodeText;
                    node.Name = binarypos.ToString();
                    subnodes.Add(node);
                    binarypos += 4;
                }
                topLevelTree.Nodes.AddRange(subnodes.ToArray());
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the staticmesh: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartStaticMeshCollectionActorScan(string nodeNameToSelect = null)
        {
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            addArrayElementButton.Visible = false;
            moveUpButton.Visible = false;
            moveDownButton.Visible = false;

            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName + " Binary");
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";

            //try
            {
                byte[] data = export.Data;
                //get a list of staticmesh stuff from the props.
                int propstart = 0x4; //we're assuming as any collection build by the engine should have started with this and i doubt any users will be making their own SMAC
                int listsize = System.BitConverter.ToInt32(data, 28);

                List<IExportEntry> smacitems = new List<IExportEntry>();

                for (int i = 0; i < listsize; i++)
                {
                    int offset = (32 + i * 4);
                    //fetch exports
                    int entryval = BitConverter.ToInt32(data, offset);
                    if (entryval > 0 && entryval < pcc.ExportCount)
                    {
                        smacitems.Add(pcc.getEntry(entryval) as IExportEntry);
                    }
                    else if (entryval == 0)
                    {
                        smacitems.Add(null);
                    }
                }

                //find start of class binary (end of props)
                int start = 0x4;
                while (start < data.Length && data.Length - 8 >= start)
                {
                    ulong nameindex = BitConverter.ToUInt64(data, start);
                    if (nameindex < (ulong)pcc.Names.Count && pcc.Names[(int)nameindex] == "None")
                    {
                        //found it
                        start += 8;
                        break;
                    }
                    else
                    {
                        start += 1;
                    }
                }

                if (data.Length - start < 4)
                {
                    TreeNode node = new TreeNode();
                    node.Tag = nodeType.Unknown;
                    node.Text = start.ToString("X4") + " Could not find end of properties (looking for none)";
                    node.Name = start.ToString();
                    topLevelTree.Nodes.Add(node);
                    treeView1.Nodes.Add(topLevelTree);
                    return;
                }

                //Lets make sure this binary is divisible by 64.
                if ((data.Length - start) % 64 != 0)
                {
                    TreeNode node = new TreeNode();
                    node.Tag = nodeType.Unknown;
                    node.Text = start.ToString("X4") + " Binary data is not divisible by 64 (" + (data.Length - start) + ")! SMCA binary data should be a length divisible by 64.";
                    node.Name = start.ToString();
                    topLevelTree.Nodes.Add(node);
                    treeView1.Nodes.Add(topLevelTree);
                    return;
                }

                int smcaindex = 0;
                while (start < data.Length && smcaindex < smacitems.Count - 1)
                {
                    TreeNode smcanode = new TreeNode();
                    smcanode.Tag = nodeType.Unknown;
                    IExportEntry assossiateddata = smacitems[smcaindex];
                    string staticmesh = "";
                    string objtext = "Null - unused data";
                    if (assossiateddata != null)
                    {
                        objtext = "[Export " + assossiateddata.Index + "] " + assossiateddata.ObjectName + "_" + assossiateddata.indexValue;

                        //find associated static mesh value for display.
                        byte[] smc_data = assossiateddata.Data;
                        int staticmeshstart = 0x4;
                        bool found = false;
                        while (staticmeshstart < smc_data.Length && smc_data.Length - 8 >= staticmeshstart)
                        {
                            ulong nameindex = BitConverter.ToUInt64(smc_data, staticmeshstart);
                            if (nameindex < (ulong)pcc.Names.Count && pcc.Names[(int)nameindex] == "StaticMesh")
                            {
                                //found it
                                found = true;
                                break;
                            }
                            else
                            {
                                staticmeshstart += 1;
                            }
                        }

                        if (found)
                        {
                            int staticmeshexp = BitConverter.ToInt32(smc_data, staticmeshstart + 0x18);
                            if (staticmeshexp > 0 && staticmeshexp < pcc.ExportCount)
                            {
                                staticmesh = pcc.getEntry(staticmeshexp).ObjectName;
                            }
                        }
                    }

                    smcanode.Text = start.ToString("X4") + " [" + smcaindex + "] " + objtext + " " + staticmesh;
                    smcanode.Name = start.ToString();
                    topLevelTree.Nodes.Add(smcanode);

                    //Read nodes
                    for (int i = 0; i < 16; i++)
                    {
                        float smcadata = BitConverter.ToSingle(data, start);
                        TreeNode node = new TreeNode();
                        node.Tag = nodeType.StructLeafFloat;
                        node.Text = start.ToString("X4");

                        string label = i.ToString();
                        switch (i)
                        {
                            case 1:
                                label = "ScalingXorY1:";
                                break;
                            case 12:
                                label = "LocX:";
                                break;
                            case 13:
                                label = "LocY:";
                                break;
                            case 14:
                                label = "LocZ:";
                                break;
                            case 15:
                                label = "CameraLayerDistance?:";
                                break;
                        }

                        node.Text += " " + label + " " + smcadata;

                        //Lookup staticmeshcomponent so we can see what this actually is without flipping
                        // export

                        node.Name = start.ToString();
                        smcanode.Nodes.Add(node);
                        start += 4;
                    }

                    smcaindex++;
                }
                treeView1.Nodes.Add(topLevelTree);
                treeView1.CollapseAll();
                topLevelTree.Expand();
                treeView1.EndUpdate();
            }
        }

        private void StartLevelScan(string nodeNameToSelect = null)
        {
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName + " Binary");
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            //try
            {
                byte[] data = export.Data;

                //find start of class binary (end of props)
                int start = 0x4;
                while (start < data.Length)
                {
                    uint nameindex = BitConverter.ToUInt32(data, start);
                    if (nameindex < pcc.Names.Count && pcc.Names[(int)nameindex] == "None")
                    {
                        //found it
                        start += 8;
                        break;
                    }
                    else
                    {
                        start += 4;
                    }
                }

                //Console.WriteLine("Found start of binary at " + start.ToString("X8"));

                uint exportid = BitConverter.ToUInt32(data, start);
                start += 4;
                uint numberofitems = BitConverter.ToUInt32(data, start);
                int countoffset = start;
                TreeNode countnode = new TreeNode();
                countnode.Tag = nodeType.Unknown;
                countnode.Text = start.ToString("X4") + " Level Items List Length: " + numberofitems;
                countnode.Name = start.ToString();
                topLevelTree.Nodes.Add(countnode);


                start += 4;
                uint bioworldinfoexportid = BitConverter.ToUInt32(data, start);
                TreeNode bionode = new TreeNode();
                bionode.Tag = nodeType.StructLeafObject;
                bionode.Text = start.ToString("X4") + " BioWorldInfo Export: " + bioworldinfoexportid;
                if (bioworldinfoexportid < pcc.ExportCount && bioworldinfoexportid > 0)
                {
                    int me3expindex = (int)bioworldinfoexportid;
                    IEntry exp = pcc.getEntry(me3expindex);
                    bionode.Text += " (" + exp.PackageFullName + "." + exp.ObjectName + ")";
                }


                bionode.Name = start.ToString();
                topLevelTree.Nodes.Add(bionode);

                IExportEntry bioworldinfo = pcc.Exports[(int)bioworldinfoexportid - 1];
                if (bioworldinfo.ObjectName != "BioWorldInfo")
                {
                    TreeNode node = new TreeNode();
                    node.Tag = nodeType.Unknown;
                    node.Text = start.ToString("X4") + " Export pointer to bioworldinfo resolves to wrong export. Resolved to " + bioworldinfo.ObjectName + " as export " + bioworldinfoexportid;
                    node.Name = start.ToString();
                    topLevelTree.Nodes.Add(node);
                    treeView1.Nodes.Add(topLevelTree);
                    return;
                }

                start += 4;
                uint shouldbezero = BitConverter.ToUInt32(data, start);
                if (shouldbezero != 0)
                {
                    TreeNode node = new TreeNode();
                    node.Tag = nodeType.Unknown;
                    node.Text = start.ToString("X4") + " Export may have extra parameters not accounted for yet (did not find 0 at 0x" + start.ToString("X5") + " )";
                    node.Name = start.ToString();
                    topLevelTree.Nodes.Add(node);
                    treeView1.Nodes.Add(topLevelTree);
                    return;
                }
                start += 4;
                int itemcount = 2; //Skip bioworldinfo and Class

                while (itemcount < numberofitems)
                {
                    //get header.
                    uint itemexportid = BitConverter.ToUInt32(data, start);
                    if (itemexportid - 1 < pcc.Exports.Count)
                    {
                        IExportEntry locexp = pcc.Exports[(int)itemexportid - 1];
                        //Console.WriteLine("0x" + start.ToString("X5") + " \t0x" + itemexportid.ToString("X5") + " \t" + locexp.PackageFullName + "." + locexp.ObjectName + "_" + locexp.indexValue + " [" + (itemexportid - 1) + "]");
                        TreeNode node = new TreeNode();
                        node.Tag = nodeType.ArrayLeafObject;
                        node.Text = start.ToString("X4") + "|" + itemcount + ": " + locexp.PackageFullName + "." + locexp.ObjectName + "_" + locexp.indexValue + " [" + (itemexportid - 1) + "]";
                        node.Name = start.ToString();
                        topLevelTree.Nodes.Add(node);
                        start += 4;
                        itemcount++;
                    }
                    else
                    {
                        Console.WriteLine("0x" + start.ToString("X5") + " \t0x" + itemexportid.ToString("X5") + " \tInvalid item. Ensure the list is the correct length. (Export " + itemexportid + ")");
                        TreeNode node = new TreeNode();
                        node.Tag = nodeType.ArrayLeafObject;
                        node.Text = start.ToString("X4") + " Invalid item.Ensure the list is the correct length. (Export " + itemexportid + ")";
                        node.Name = start.ToString();
                        topLevelTree.Nodes.Add(node);
                        start += 4;
                        itemcount++;
                    }
                }

                treeView1.Nodes.Add(topLevelTree);
                treeView1.CollapseAll();
                treeView1.Nodes[0].Expand();
                TreeNode[] nodes;
                if (nodeNameToSelect != null)
                {
                    //Needs fixed up
                    nodes = topLevelTree.Nodes.Find(nodeNameToSelect, true);
                    if (nodes.Length > 0)
                    {
                        treeView1.SelectedNode = nodes[0];
                    }
                    else
                    {
                        treeView1.SelectedNode = treeView1.Nodes[0];
                    }
                }

                treeView1.EndUpdate();
                topLevelTree.Expand();
                memsize = memory.Length;

            }
            //catch (Exception e)
            //{
            //  topLevelTree.Nodes.Add("Error parsing level: " + e.Message);
            //}
        }



        private void StartClassScan(string nodeNameToSelect = null)
        {
            const int nonTableEntryCount = 2; //how many items we parse that are not part of the functions table. e.g. the count, the defaults pointer
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                List<TreeNode> subnodes = ReadTableBackwards(export);
                subnodes.Reverse();
                for (int i = nonTableEntryCount; i < subnodes.Count; i++)
                {
                    string text = subnodes[i].Text;
                    text = (i - nonTableEntryCount) + " | " + text;
                    subnodes[i].Text = text;
                }
                topLevelTree.Nodes.AddRange(subnodes.ToArray());
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the class: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            //if (expandedNodes != null)
            //{
            //    int memDiff = memory.Length - memsize;
            //    int selectedPos = getPosFromNode(selectedNodeName);
            //    int curPos = 0;
            //    foreach (string item in expandedNodes)
            //    {
            //        curPos = getPosFromNode(item);
            //        if (curPos > selectedPos)
            //        {
            //            curPos += memDiff;
            //        }
            //        nodes = treeView1.Nodes.Find((item[0] == '-' ? -curPos : curPos).ToString(), true);
            //        if (nodes.Length > 0)
            //        {
            //            foreach (var node in nodes)
            //            {
            //                node.Expand();
            //            }
            //        }
            //    }
            //}
            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private void StartClassScan2(string nodeNameToSelect = null)
        {
            //const int nonTableEntryCount = 2; //how many items we parse that are not part of the functions table. e.g. the count, the defaults pointer
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                TreeNode node;

                byte[] data = export.Data;
                int offset = 0;

                int unrealExportIndex = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unreal Unique Index: " + unrealExportIndex);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;


                int superclassIndex = BitConverter.ToInt32(data, offset);
                string superclassStr = getEntryFullPath(superclassIndex);

                node = new TreeNode("0x" + offset.ToString("X5") + " Superclass Index: " + superclassIndex + "(" + superclassStr + ")");
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafObject;

                topLevelTree.Nodes.Add(node);
                offset += 4;

                int unknown1 = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Unknown 1: " + unknown1);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                int classObjTree = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " ProbeMask/Class Object Tree Final Pointer Index: " + classObjTree);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;


                //I am not sure what these mean. However if Pt1&2 are 33/25, the following bytes that follow are extended.
                int headerUnknown1 = BitConverter.ToInt32(data, offset);
                Int64 ignoreMask = BitConverter.ToInt64(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " IgnoreMask: 0x" + ignoreMask.ToString("X16"));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 8;

                Int16 labelOffset = BitConverter.ToInt16(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " LabelOffset: 0x" + labelOffset.ToString("X4"));
                node.Name = offset.ToString();
                topLevelTree.Nodes.Add(node);
                offset += 2;

                int skipAmount = 0x6;
                //Find end of script block. Seems to be 10 FF's.
                while (offset + skipAmount + 10 < data.Length)
                {
                    //Debug.WriteLine("Cheecking at 0x"+(offset + skipAmount + 10).ToString("X4"));
                    bool isEnd = true;
                    for (int i = 0; i < 10; i++)
                    {
                        byte b = data[offset + skipAmount + i];
                        if (b != 0xFF)
                        {
                            isEnd = false;
                            break;
                        }
                    }
                    if (isEnd)
                    {
                        break;
                    }
                    else
                    {
                        skipAmount++;
                    }
                }
                //if (headerUnknown1 == 33 && headerUnknown2 == 25)
                //{
                //    skipAmount = 0x2F;
                //}
                //else if (headerUnknown1 == 34 && headerUnknown2 == 26)
                //{
                //    skipAmount = 0x30;
                //}
                //else if (headerUnknown1 == 728 && headerUnknown2 == 532)
                //{
                //    skipAmount = 0x22A;
                //}
                int offsetEnd = offset + skipAmount + 10;
                node = new TreeNode("0x" + offset.ToString("X5") + " State/Script Block: 0x" + offset.ToString("X4") + " - 0x" + offsetEnd.ToString("X4"));
                node.Name = offset.ToString();
                topLevelTree.Nodes.Add(node);
                offset += skipAmount + 10; //heuristic to find end of script
                //for (int i = 0; i < 5; i++)
                //{
                uint stateMask = BitConverter.ToUInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Statemask: " + stateMask + " [" + getStateFlagsStr(stateMask) + "]");
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;
                //}
                //offset += 2; //oher unknown
                int localFunctionsTableCount = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Local Functions Count: " + localFunctionsTableCount);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;
                for (int i = 0; i < localFunctionsTableCount; i++)
                {
                    int nameTableIndex = BitConverter.ToInt32(data, offset);
                    int nameIndex = BitConverter.ToInt32(data, offset + 4);
                    offset += 8;
                    int functionObjectIndex = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    TreeNode subnode = new TreeNode("0x" + (offset - 12).ToString("X5") + "  " + export.FileRef.getNameEntry(nameTableIndex) + "() = " + functionObjectIndex + "(" + export.FileRef.Exports[functionObjectIndex - 1].GetFullPath + ")");
                    subnode.Name = (offset - 12).ToString();
                    subnode.Tag = nodeType.StructLeafName; //might need to add a subnode for the 3rd int
                    node.Nodes.Add(subnode);
                }

                int classMask = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Class Mask: 0x" + classMask.ToString("X8"));
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                if (export.FileRef.Game != MEGame.ME3)
                {
                    offset += 1; //seems to be a blank byte here
                }

                int coreReference = BitConverter.ToInt32(data, offset);
                string coreRefFullPath = getEntryFullPath(coreReference);

                node = new TreeNode("0x" + offset.ToString("X5") + " Outer Class: " + coreReference + " (" + coreRefFullPath + ")");
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafObject;
                topLevelTree.Nodes.Add(node);
                offset += 4;


                if (export.FileRef.Game == MEGame.ME3)
                {
                    offset = ClassParser_ReadComponentsTable(topLevelTree, data, offset);
                    offset = ClassParser_ReadImplementsTable(topLevelTree, data, offset);
                    int postComponentsNoneNameIndex = BitConverter.ToInt32(data, offset);
                    int postComponentNoneIndex = BitConverter.ToInt32(data, offset + 4);
                    string postCompName = export.FileRef.getNameEntry(postComponentsNoneNameIndex); //This appears to be unused in ME#, it is always None it seems.
                    /*if (postCompName != "None")
                    {
                        Debugger.Break();
                    }*/
                    node = new TreeNode("0x" + offset.ToString("X5") + " Post-Components Blank (" + postCompName + ")");
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafName;
                    topLevelTree.Nodes.Add(node);
                    offset += 8;

                    int unknown4 = BitConverter.ToInt32(data, offset);
                    /*if (unknown4 != 0)
                    {
                        Debug.WriteLine("Unknown 4 is not 0: " + unknown4);
                       // Debugger.Break();
                    }*/
                    node = new TreeNode("0x" + offset.ToString("X5") + " Unknown 4: " + unknown4);
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafInt;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;
                }
                else
                {
                    offset = ClassParser_ReadImplementsTable(topLevelTree, data, offset);
                    offset = ClassParser_ReadComponentsTable(topLevelTree, data, offset);

                    /*int unknown4 = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " Unknown 4: " + unknown4);
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafInt;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;*/

                    int me12unknownend1 = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " ME1/ME2 Unknown1: " + me12unknownend1);
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafName;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;

                    int me12unknownend2 = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " ME1/ME2 Unknown2: " + me12unknownend2);
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafName;
                    topLevelTree.Nodes.Add(node);
                    offset += 4;
                }

                int defaultsClassLink = BitConverter.ToInt32(data, offset);
                node = new TreeNode("0x" + offset.ToString("X5") + " Class Defaults: " + defaultsClassLink + " (" + export.FileRef.Exports[defaultsClassLink - 1].GetFullPath + ")");
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafObject;

                topLevelTree.Nodes.Add(node);
                offset += 4;

                if (export.FileRef.Game == MEGame.ME3)
                {
                    int functionsTableCount = BitConverter.ToInt32(data, offset);
                    node = new TreeNode("0x" + offset.ToString("X5") + " Full Functions Table Count: " + functionsTableCount);
                    node.Name = offset.ToString();
                    node.Tag = nodeType.StructLeafInt;

                    topLevelTree.Nodes.Add(node);
                    offset += 4;

                    for (int i = 0; i < functionsTableCount; i++)
                    {
                        int functionsTableIndex = BitConverter.ToInt32(data, offset);
                        string impexpName = getEntryFullPath(functionsTableIndex);
                        TreeNode subnode = new TreeNode("0x" + offset.ToString("X5") + " " + impexpName);
                        subnode.Tag = nodeType.StructLeafObject;
                        subnode.Name = offset.ToString();
                        node.Nodes.Add(subnode);
                        offset += 4;
                    }
                }
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the class: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;

            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private int ClassParser_ReadComponentsTable(TreeNode topLevelTree, byte[] data, int offset)
        {
            if (export.FileRef.Game == MEGame.ME3)
            {
                int componentTableNameIndex = BitConverter.ToInt32(data, offset);
                int componentTableIndex = BitConverter.ToInt32(data, offset + 4);
                offset += 8;

                TreeNode node = new TreeNode("0x" + (offset - 8).ToString("X5") + " Components Table (" + export.FileRef.getNameEntry(componentTableNameIndex) + ")");
                node.Name = (offset - 8).ToString();
                node.Tag = nodeType.StructLeafName;
                topLevelTree.Nodes.Add(node);
                int componentTableCount = BitConverter.ToInt32(data, offset);
                offset += 4;

                for (int i = 0; i < componentTableCount; i++)
                {
                    int nameTableIndex = BitConverter.ToInt32(data, offset);
                    int nameIndex = BitConverter.ToInt32(data, offset + 4);
                    offset += 8;
                    int componentObjectIndex = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    string objectName = getEntryFullPath(componentObjectIndex);
                    TreeNode subnode = new TreeNode("0x" + (offset - 12).ToString("X5") + "  " + export.FileRef.getNameEntry(nameTableIndex) + "(" + objectName + ")");
                    subnode.Name = (offset - 12).ToString();
                    subnode.Tag = nodeType.StructLeafName;
                    node.Nodes.Add(subnode);
                }
            }
            else
            {
                int componentTableCount = BitConverter.ToInt32(data, offset);
                TreeNode node = new TreeNode("0x" + offset.ToString("X5") + " Components Table Count: " + componentTableCount);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;

                for (int i = 0; i < componentTableCount; i++)
                {
                    int nameTableIndex = BitConverter.ToInt32(data, offset);
                    int nameIndex = BitConverter.ToInt32(data, offset + 4);
                    offset += 8;
                    int componentObjectIndex = BitConverter.ToInt32(data, offset);

                    string objName = "Null";
                    if (componentObjectIndex != 0)
                    {
                        objName = getEntryFullPath(componentObjectIndex);
                    }
                    TreeNode subnode = new TreeNode("0x" + (offset - 8).ToString("X5") + "  " + export.FileRef.getNameEntry(nameTableIndex) + "(" + objName + ")");
                    subnode.Name = (offset - 8).ToString();
                    subnode.Tag = nodeType.StructLeafName;
                    node.Nodes.Add(subnode);
                    offset += 4;

                }
            }
            return offset;
        }

        private int ClassParser_ReadImplementsTable(TreeNode topLevelTree, byte[] data, int offset)
        {
            if (export.FileRef.Game == MEGame.ME3)
            {
                int interfaceCount = BitConverter.ToInt32(data, offset);

                TreeNode node = new TreeNode("0x" + offset.ToString("X5") + " Implemented Interfaces Table Count: " + interfaceCount);
                node.Name = offset.ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;
                for (int i = 0; i < interfaceCount; i++)
                {
                    int interfaceIndex = BitConverter.ToInt32(data, offset);
                    offset += 4;

                    string objectName = getEntryFullPath(interfaceIndex);
                    TreeNode subnode = new TreeNode("0x" + (offset - 12).ToString("X5") + "  " + interfaceIndex + " " + objectName);
                    subnode.Name = (offset - 4).ToString();
                    subnode.Tag = nodeType.StructLeafName;
                    node.Nodes.Add(subnode);

                    //propertypointer
                    interfaceIndex = BitConverter.ToInt32(data, offset);
                    offset += 4;

                    objectName = getEntryFullPath(interfaceIndex);
                    TreeNode subsubnode = new TreeNode("0x" + (offset - 12).ToString("X5") + "  Interface Property Link: " + interfaceIndex + " " + objectName);
                    subsubnode.Name = (offset - 4).ToString();
                    subsubnode.Tag = nodeType.StructLeafObject;
                    subnode.Nodes.Add(subsubnode);
                }
            }
            else
            {
                int interfaceTableName = BitConverter.ToInt32(data, offset); //????
                offset += 8;

                int interfaceCount = BitConverter.ToInt32(data, offset);
                TreeNode node = new TreeNode("0x" + (offset - 8).ToString("X5") + " Implemented Interfaces Table Count: " + interfaceCount + " (" + pcc.getNameEntry(interfaceTableName) + ")");
                node.Name = (offset - 8).ToString();
                node.Tag = nodeType.StructLeafInt;
                topLevelTree.Nodes.Add(node);
                offset += 4;
                for (int i = 0; i < interfaceCount; i++)
                {
                    int interfaceNameIndex = BitConverter.ToInt32(data, offset);
                    offset += 8;

                    TreeNode subnode = new TreeNode("0x" + (offset - 8).ToString("X5") + "  " + export.FileRef.getNameEntry(interfaceNameIndex));
                    subnode.Name = (offset - 8).ToString();
                    subnode.Tag = nodeType.StructLeafName;
                    node.Nodes.Add(subnode);

                    //propertypointer
                    /* interfaceIndex = BitConverter.ToInt32(data, offset);
                     offset += 4;

                     objectName = getEntryFullPath(interfaceIndex);
                     TreeNode subsubnode = new TreeNode("0x" + (offset - 12).ToString("X5") + "  Interface Property Link: " + interfaceIndex + " " + objectName);
                     subsubnode.Name = (offset - 4).ToString();
                     subsubnode.Tag = nodeType.StructLeafObject;
                     subnode.Nodes.Add(subsubnode);
                     */
                }
            }
            return offset;
        }

        public enum StateFlags : uint
        {
            None = 0,
            Editable = 0x00000001U,
            Auto = 0x00000002U,
            Simulated = 0x00000004U,
        }

        private string getStateFlagsStr(uint stateFlags)
        {
            string str = "";
            if (stateFlags == 0)
            {
                return "None";
            }
            if ((stateFlags & (uint)StateFlags.Editable) != 0)
            {
                str += "Editable ";
            }
            if ((stateFlags & (uint)StateFlags.Auto) != 0)
            {
                str += "Auto";
            }
            if ((stateFlags & (uint)StateFlags.Editable) != 0)
            {
                str += "Simulated";
            }
            return str;
        }

        private string getEntryFullPath(int index)
        {
            if (index == 0)
            {
                return "Null";
            }
            string retStr = "Entry not found";
            IEntry coreRefEntry = getUnrealEntry(index);
            if (coreRefEntry != null)
            {
                if (coreRefEntry is ImportEntry)
                {
                    retStr = "[I] ";
                }
                else
                {
                    retStr = "[E] ";
                }
                retStr += coreRefEntry.GetFullPath;
            }
            return retStr;
        }

        private IEntry getUnrealEntry(int entryIndex)
        {
            if (entryIndex < 0 && -entryIndex - 1 < export.FileRef.Imports.Count)
            {
                //import
                int localindex = Math.Abs(entryIndex) - 1;
                return export.FileRef.Imports[localindex];
            }
            else if (entryIndex > 0 && entryIndex < export.FileRef.Exports.Count)
            {
                //import
                int localindex = entryIndex - 1;
                return export.FileRef.Exports[localindex];
            }
            return null;
        }

        private void StartMaterialScan(string nodeNameToSelect = null)
        {
            const int nonTableEntryCount = 2; //how many items we parse that are not part of the functions table. e.g. the count, the defaults pointer
            resetPropEditingControls();
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();


            byte[] data = export.Data;

            int binarystart = findEndOfProps();
            //find start of class binary (end of props). This should 



            TreeNode topLevelTree = new TreeNode("0000 : " + export.ObjectName);
            topLevelTree.Tag = nodeType.Root;
            topLevelTree.Name = "0";
            try
            {
                int binarypos = binarystart;
                List<TreeNode> subnodes = new List<TreeNode>();

                binarypos += 0x1C; //Skip ??? and GUID
                                   //int guid = BitConverter.ToInt32(data, binarypos);
                int num1 = BitConverter.ToInt32(data, binarypos);
                TreeNode node = new TreeNode("0x" + binarypos.ToString("X4") + " ???: " + num1.ToString());
                subnodes.Add(node);
                binarypos += 4;
                int num2 = BitConverter.ToInt32(data, binarypos);
                node = new TreeNode("0x" + binarypos.ToString("X4") + " Count: " + num2.ToString());
                subnodes.Add(node);
                binarypos += 4;

                while (binarypos <= data.Length - 4)
                {
                    int val = BitConverter.ToInt32(data, binarypos);
                    string name = val.ToString();
                    if (val > 0 && val <= pcc.Exports.Count)
                    {
                        IExportEntry exp = pcc.Exports[val - 1];
                        name += " " + exp.PackageFullName + "." + exp.ObjectName + " (" + exp.ClassName + ")";
                    }
                    else if (val < 0 && Math.Abs(val) <= pcc.Imports.Count)
                    {
                        int csImportVal = Math.Abs(val) - 1;
                        ImportEntry imp = pcc.Imports[csImportVal];
                        name += " " + imp.PackageFullName + "." + imp.ObjectName + " (" + imp.ClassName + ")";

                    }

                    node = new TreeNode("0x" + binarypos.ToString("X4") + " " + name);
                    node.Tag = nodeType.StructLeafObject;
                    node.Name = binarypos.ToString();
                    subnodes.Add(node);
                    binarypos += 4;
                }
                topLevelTree.Nodes.AddRange(subnodes.ToArray());
            }
            catch (Exception ex)
            {
                topLevelTree.Nodes.Add("An error occured parsing the material: " + ex.Message);
            }
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
            TreeNode[] nodes;
            if (nodeNameToSelect != null)
            {
                nodes = treeView1.Nodes.Find(nodeNameToSelect, true);
                if (nodes.Length > 0)
                {
                    treeView1.SelectedNode = nodes[0];
                }
                else
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
            }

            treeView1.EndUpdate();
            memsize = memory.Length;
        }

        private int findEndOfProps()
        {
            return export.propsEnd();
            //int readerpos = export.GetPropertyStart();
            //bool run = true;
            //while (run)
            //{
            //    PropHeader p = new PropHeader();
            //    if (readerpos > memory.Length || readerpos < 0)
            //    {
            //        //nothing else to interpret.
            //        run = false;
            //        readerpos = -1;
            //        continue;
            //    }
            //    p.name = BitConverter.ToInt32(memory, readerpos);
            //    if (!pcc.isName(p.name))
            //        run = false;
            //    else
            //    {
            //        if (pcc.getNameEntry(p.name) != "None")
            //        {
            //            p.type = BitConverter.ToInt32(memory, readerpos + 8);
            //            nodeType type = getType(pcc.getNameEntry(p.type));
            //            bool isUnknownType = type == nodeType.Unknown;
            //            if (!pcc.isName(p.type) || isUnknownType)
            //                run = false;
            //            else
            //            {
            //                p.size = BitConverter.ToInt32(memory, readerpos + 16);
            //                readerpos += p.size + 24;

            //                if (getType(pcc.getNameEntry(p.type)) == nodeType.StructProperty) //StructName
            //                    readerpos += 8;
            //                if (pcc.Game == MEGame.ME3)
            //                {
            //                    if (getType(pcc.getNameEntry(p.type)) == nodeType.BoolProperty)//Boolbyte
            //                        readerpos++;
            //                    if (getType(pcc.getNameEntry(p.type)) == nodeType.ByteProperty)//byteprop
            //                        readerpos += 8;
            //                }
            //                else
            //                {
            //                    if (getType(pcc.getNameEntry(p.type)) == nodeType.BoolProperty)
            //                        readerpos += 4;
            //                }
            //            }
            //        }
            //        else
            //        {
            //            readerpos += 8;
            //            run = false;
            //        }
            //    }
            //}
            //return readerpos;
        }

        private List<TreeNode> ReadTableBackwards(IExportEntry export)
        {
            List<TreeNode> tableItems = new List<TreeNode>();

            byte[] data = export.Data;
            int endOffset = data.Length;
            int count = 0;
            endOffset -= 4; //int
            while (endOffset > 0)
            {
                int index = BitConverter.ToInt32(data, endOffset);
                if (index < 0 && -index - 1 < pcc.Imports.Count)
                {
                    //import
                    int localindex = Math.Abs(index) - 1;
                    TreeNode node = new TreeNode();
                    node.Tag = nodeType.ArrayLeafObject;
                    node.Text = "0x" + endOffset.ToString("X4") + " [I] " + pcc.Imports[localindex].PackageFullName + "." + pcc.Imports[localindex].ObjectName;
                    node.Name = endOffset.ToString();
                    tableItems.Add(node);
                }
                else if (index > 0 && index != count)
                {
                    int localindex = index - 1;
                    TreeNode node = new TreeNode();
                    node.Tag = nodeType.ArrayLeafObject;
                    node.Name = endOffset.ToString();
                    node.Text = "0x" + endOffset.ToString("X4") + " [E] " + pcc.Exports[localindex].PackageFullName + "." + pcc.Exports[localindex].ObjectName + "_" + pcc.Exports[localindex].indexValue;
                    tableItems.Add(node);
                }
                else
                {
                    //Console.WriteLine("UNPARSED INDEX: " + index);
                }
                //Console.WriteLine(index);
                if (index == count)
                {
                    {
                        TreeNode node = new TreeNode();
                        node.Tag = nodeType.StructLeafInt;
                        node.Name = endOffset.ToString();
                        node.Text = endOffset.ToString("X4") + " Class Functions Table Count";
                        tableItems.Add(node);
                    }
                    endOffset -= 4;
                    if (endOffset > 0)
                    {
                        TreeNode node = new TreeNode();
                        node.Tag = nodeType.StructLeafObject;
                        node.Name = endOffset.ToString();
                        string defaults = "";
                        int defaultsindex = BitConverter.ToInt32(data, endOffset);
                        if (defaultsindex < 0 && -index - 1 < pcc.Imports.Count)
                        {
                            defaultsindex = Math.Abs(defaultsindex) - 1;
                            defaults = pcc.Imports[defaultsindex].PackageFullName + "." + pcc.Imports[defaultsindex].ObjectName;
                        }
                        else if (defaultsindex > 0 && defaultsindex - 1 < pcc.Exports.Count)
                        {
                            defaults = pcc.Exports[defaultsindex - 1].PackageFullName + "." + pcc.Exports[defaultsindex - 1].ObjectName;
                        }

                        node.Text = endOffset.ToString("X4") + " Class Defaults | " + defaults;
                        tableItems.Add(node);
                    }
                    //Console.WriteLine("FOUND START OF LIST AT 0x" + endOffset.ToString("X5") + " , items: " + index);
                    break;
                }
                endOffset -= 4;
                count++;
            }

            //Console.WriteLine("Number of items processed: " + count);
            return tableItems;
        }

        public new void Show()
        {
            base.Show();
            //StartScan();
        }

        public nodeType getType(string s)
        {
            int ret = -1;
            for (int i = 0; i < Types.Length; i++)
                if (s == Types[i])
                    ret = i;
            return (nodeType)ret;
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = "*.txt|*.txt";
            d.FileName = export.ObjectName + ".txt";
            if (d.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(d.FileName, FileMode.Create, FileAccess.Write);
                PrintNodes(treeView1.Nodes, fs, 0);
                fs.Close();
                MessageBox.Show("Done.");
            }
        }

        public void PrintNodes(TreeNodeCollection t, FileStream fs, int depth)
        {
            string tab = "";
            for (int i = 0; i < depth; i++)
                tab += ' ';
            foreach (TreeNode t1 in t)
            {
                string s = tab + t1.Text;
                WriteString(fs, s);
                fs.WriteByte(0xD);
                fs.WriteByte(0xA);
                if (t1.Nodes.Count != 0)
                    PrintNodes(t1.Nodes, fs, depth + 4);
            }
        }

        public void WriteString(FileStream fs, string s)
        {
            for (int i = 0; i < s.Length; i++)
                fs.WriteByte((byte)s[i]);
        }

        private string getEnclosingType(TreeNode node)
        {
            Stack<TreeNode> nodeStack = new Stack<TreeNode>();
            string typeName = className;
            string propname;
            PropertyInfo p;
            while (node != null && !node.Tag.Equals(nodeType.Root))
            {
                nodeStack.Push(node);
                node = node.Parent;
            }
            bool isStruct = false;
            while (nodeStack.Count > 0)
            {
                node = nodeStack.Pop();
                if ((nodeType)node.Tag == nodeType.ArrayLeafStruct)
                {
                    continue;
                }
                propname = pcc.getNameEntry(BitConverter.ToInt32(memory, getPosFromNode(node.Name)));
                p = GetPropertyInfo(propname, typeName, isStruct);
                typeName = p.reference;
                isStruct = true;
            }
            return typeName;
        }
        private bool isArrayLeaf(nodeType type)
        {
            return (type == nodeType.ArrayLeafBool || type == nodeType.ArrayLeafEnum || type == nodeType.ArrayLeafFloat ||
                type == nodeType.ArrayLeafInt || type == nodeType.ArrayLeafName || type == nodeType.ArrayLeafObject ||
                type == nodeType.ArrayLeafString || type == nodeType.ArrayLeafStruct || type == nodeType.ArrayLeafByte);
        }

        private bool isStructLeaf(nodeType type)
        {
            return (type == nodeType.StructLeafByte || type == nodeType.StructLeafDeg || type == nodeType.StructLeafFloat ||
                type == nodeType.StructLeafBool || type == nodeType.StructLeafInt || type == nodeType.StructLeafName ||
                type == nodeType.StructLeafStr || type == nodeType.StructLeafEnum || type == nodeType.StructLeafArray ||
                type == nodeType.StructLeafStruct || type == nodeType.StructLeafObject);
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            LAST_SELECTED_NODE = e.Node;
            resetPropEditingControls();
            if (e.Node.Name == "")
            {
                Debug.WriteLine("This node is not parsable.");
                //can't attempt to parse this.
                LAST_SELECTED_PROP_TYPE = nodeType.Unknown;
                return;
            }
            try
            {
                int off = getPosFromNode(e.Node.Name);
                hb1.SelectionStart = off;
                lastSetOffset = off;
                hb1.SelectionLength = 1;
                if (e.Node.Tag == null)
                {
                    LAST_SELECTED_PROP_TYPE = nodeType.Unknown;
                    return;
                }
                LAST_SELECTED_PROP_TYPE = (nodeType)e.Node.Tag;
                if (isArrayLeaf(LAST_SELECTED_PROP_TYPE) || isStructLeaf(LAST_SELECTED_PROP_TYPE))
                {
                    TryParseStructPropertyOrArrayLeaf(e.Node);
                }
                else if (LAST_SELECTED_PROP_TYPE == nodeType.ArrayProperty)
                {
                    addArrayElementButton.Visible = true;
                    proptext.Clear();
                    ArrayType arrayType = GetArrayType(BitConverter.ToInt32(memory, off), getEnclosingType(e.Node.Parent));
                    switch (arrayType)
                    {
                        case ArrayType.Byte:
                        case ArrayType.String:
                            proptext.Visible = true;
                            break;
                        case ArrayType.Object:
                            objectNameLabel.Text = "()";
                            proptext.Visible = objectNameLabel.Visible = true;
                            break;
                        case ArrayType.Int:
                            proptext.Text = "0";
                            proptext.Visible = true;
                            break;
                        case ArrayType.Float:
                            proptext.Text = "0.0";
                            proptext.Visible = true;
                            break;
                        case ArrayType.Name:
                            proptext.Text = "0";
                            nameEntry.AutoCompleteCustomSource.AddRange(pcc.Names.ToArray());
                            proptext.Visible = nameEntry.Visible = true;
                            break;
                        case ArrayType.Bool:
                            propDropdown.Items.Clear();
                            propDropdown.Items.Add("False");
                            propDropdown.Items.Add("True");
                            propDropdown.Visible = true;
                            break;
                        case ArrayType.Enum:
                            string enumName = getEnclosingType(e.Node);
                            List<string> values = GetEnumValues(enumName, BitConverter.ToInt32(memory, getPosFromNode(e.Node.Parent.Name)));
                            if (values == null)
                            {
                                addArrayElementButton.Visible = false;
                                return;
                            }
                            propDropdown.Items.Clear();
                            propDropdown.Items.AddRange(values.ToArray());
                            propDropdown.Visible = true;
                            break;
                        case ArrayType.Struct:
                        default:
                            break;
                    }
                }
                else
                {
                    TryParseProperty();
                }
            }
            catch (Exception ep)
            {
                Debug.WriteLine("Node name is not in correct format.");
                //name is wrong, don't attempt to continue parsing.
                LAST_SELECTED_PROP_TYPE = nodeType.Unknown;
                return;
            }
        }
        private void resetPropEditingControls()
        {
            objectNameLabel.Visible = nameEntry.Visible = proptext.Visible = setPropertyButton.Visible = propDropdown.Visible =
                addArrayElementButton.Visible = deleteArrayElementButton.Visible = moveDownButton.Visible =
                moveUpButton.Visible = false;
            nameEntry.AutoCompleteCustomSource.Clear();
            nameEntry.Clear();
            proptext.Clear();
        }

        private void TryParseProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int type = BitConverter.ToInt32(memory, pos + 8);
                int test = BitConverter.ToInt32(memory, pos + 12);
                if (test != 0 || !pcc.isName(type))
                    return;
                switch (pcc.getNameEntry(type))
                {
                    case "IntProperty":
                    case "StringRefProperty":
                        proptext.Text = BitConverter.ToInt32(memory, pos + 24).ToString();
                        proptext.Visible = true;
                        break;
                    case "ObjectProperty":
                        int n = BitConverter.ToInt32(memory, pos + 24);
                        objectNameLabel.Text = $"({pcc.getObjectName(n)})";
                        proptext.Text = n.ToString();
                        objectNameLabel.Visible = proptext.Visible = true;
                        break;
                    case "FloatProperty":
                        proptext.Text = BitConverter.ToSingle(memory, pos + 24).ToString();
                        proptext.Visible = true;
                        break;
                    case "BoolProperty":
                        propDropdown.Items.Clear();
                        propDropdown.Items.Add("False");
                        propDropdown.Items.Add("True");
                        propDropdown.SelectedIndex = memory[pos + 24];
                        propDropdown.Visible = true;
                        break;
                    case "NameProperty":
                        proptext.Text = BitConverter.ToInt32(memory, pos + 28).ToString();
                        nameEntry.Text = pcc.getNameEntry(BitConverter.ToInt32(memory, pos + 24));
                        nameEntry.AutoCompleteCustomSource.AddRange(pcc.Names.ToArray());
                        nameEntry.Visible = true;
                        proptext.Visible = true;
                        break;
                    case "StrProperty":
                        string s = "";
                        int count = BitConverter.ToInt32(memory, pos + 24);
                        pos += 28;
                        if (count < 0)
                        {
                            for (int i = 0; i < -count; i++)
                            {
                                s += (char)memory[pos + i * 2];
                            }
                        }
                        else
                        {
                            for (int i = 0; i < count; i++)
                            {
                                s += (char)memory[pos + i];
                            }
                        }
                        proptext.Text = s;
                        proptext.Visible = true;
                        break;
                    case "ByteProperty":
                        int size = BitConverter.ToInt32(memory, pos + 16);
                        string enumName = pcc.getNameEntry(BitConverter.ToInt32(memory, pos + 24));
                        int valOffset;
                        if (pcc.Game == MEGame.ME3)
                        {
                            valOffset = 32;
                        }
                        else
                        {
                            valOffset = 24;
                        }
                        if (size > 1)
                        {
                            try
                            {
                                List<string> values = GetEnumValues(enumName, BitConverter.ToInt32(memory, pos));
                                if (values != null)
                                {
                                    propDropdown.Items.Clear();
                                    propDropdown.Items.AddRange(values.ToArray());
                                    propDropdown.SelectedItem = pcc.getNameEntry(BitConverter.ToInt32(memory, pos + valOffset));
                                    propDropdown.Visible = true;
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                        else
                        {
                            proptext.Text = memory[pos + valOffset].ToString();
                            proptext.Visible = true;
                        }
                        break;
                    default:
                        return;
                }
                setPropertyButton.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void TryParseStructPropertyOrArrayLeaf(TreeNode node)
        {
            try
            {
                nodeType type = (nodeType)node.Tag;
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 4 && type == nodeType.StructLeafObject)
                {
                    return;
                }
                else if (memory.Length - pos < 8 && type == nodeType.StructLeafName)
                {
                    return;
                }
                switch (type)
                {
                    case nodeType.ArrayLeafInt:
                    case nodeType.StructLeafInt:
                        proptext.Text = BitConverter.ToInt32(memory, pos).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafObject:
                    case nodeType.StructLeafObject:
                        int n = BitConverter.ToInt32(memory, pos);
                        objectNameLabel.Text = $"({pcc.getObjectName(n)})";
                        proptext.Text = n.ToString();
                        proptext.Visible = objectNameLabel.Visible = true;
                        break;
                    case nodeType.ArrayLeafFloat:
                    case nodeType.StructLeafFloat:
                        proptext.Text = BitConverter.ToSingle(memory, pos).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafBool:
                    case nodeType.StructLeafBool:
                        propDropdown.Items.Clear();
                        propDropdown.Items.Add("False");
                        propDropdown.Items.Add("True");
                        propDropdown.SelectedIndex = memory[pos];
                        propDropdown.Visible = true;
                        break;
                    case nodeType.ArrayLeafByte:
                    case nodeType.StructLeafByte:
                        proptext.Text = memory[pos].ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafName:
                    case nodeType.StructLeafName:
                        proptext.Text = BitConverter.ToInt32(memory, pos + 4).ToString();
                        nameEntry.Text = pcc.getNameEntry(BitConverter.ToInt32(memory, pos));
                        nameEntry.AutoCompleteCustomSource.AddRange(pcc.Names.ToArray());
                        nameEntry.Visible = proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafString:
                    case nodeType.StructLeafStr:
                        string s = "";
                        int count = -BitConverter.ToInt32(memory, pos);
                        for (int i = 0; i < count - 1; i++)
                        {
                            s += (char)memory[pos + 4 + i * 2];
                        }
                        proptext.Text = s;
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafEnum:
                    case nodeType.StructLeafEnum:
                        string enumName;
                        if (type == nodeType.StructLeafEnum)
                        {
                            int begin = node.Text.LastIndexOf(':') + 3;
                            enumName = node.Text.Substring(begin, node.Text.IndexOf(',') - 1 - begin);
                        }
                        else
                        {
                            enumName = getEnclosingType(node.Parent);
                        }
                        List<string> values = GetEnumValues(enumName, BitConverter.ToInt32(memory, getPosFromNode(node.Parent)));
                        if (values == null)
                        {
                            return;
                        }
                        propDropdown.Items.Clear();
                        propDropdown.Items.AddRange(values.ToArray());
                        setPropertyButton.Visible = propDropdown.Visible = true;
                        propDropdown.SelectedItem = pcc.getNameEntry(BitConverter.ToInt32(memory, pos));
                        break;
                    case nodeType.StructLeafDeg:
                        proptext.Text = (BitConverter.ToInt32(memory, pos) * 360f / 65536f).ToString();
                        proptext.Visible = true;
                        break;
                    case nodeType.ArrayLeafStruct:
                        break;
                    default:
                        return;
                }
                setPropertyButton.Visible = true;
                if (isArrayLeaf(type))
                {
                    deleteArrayElementButton.Visible = addArrayElementButton.Visible = true;
                    if (type == nodeType.ArrayLeafStruct)
                    {
                        setPropertyButton.Visible = false;
                    }
                    if (node.NextNode != null)
                    {
                        moveDownButton.Visible = true;
                    }
                    if (node.PrevNode != null)
                    {
                        moveUpButton.Visible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setProperty_Click(object sender, EventArgs e)
        {
            if (hb1.SelectionStart != lastSetOffset)
            {
                return; //user manually moved cursor
            }
            if (isArrayLeaf(LAST_SELECTED_PROP_TYPE) || isStructLeaf(LAST_SELECTED_PROP_TYPE))
            {
                setStructOrArrayProperty();
            }
            else
            {
                setNonArrayProperty();
            }
            //UpdateMem();
            RefreshMem();
        }

        private void setStructOrArrayProperty()
        {
            try
            {
                int pos = lastSetOffset;
                if (memory.Length - pos < 4)
                    return;
                byte b = 0;
                float f = 0;
                int i = 0;
                switch (LAST_SELECTED_PROP_TYPE)
                {
                    case nodeType.ArrayLeafByte:
                    case nodeType.StructLeafByte:
                        if (byte.TryParse(proptext.Text, out b))
                        {
                            memory[pos] = b;
                            UpdateMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafBool:
                    case nodeType.StructLeafBool:
                        memory[pos] = (byte)propDropdown.SelectedIndex;
                        UpdateMem(pos);
                        break;
                    case nodeType.ArrayLeafFloat:
                    case nodeType.StructLeafFloat:
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos, BitConverter.GetBytes(f));
                            UpdateMem(pos);
                        }
                        break;
                    case nodeType.StructLeafDeg:
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos, BitConverter.GetBytes(Convert.ToInt32(f * 65536f / 360f)));
                            UpdateMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafInt:
                    case nodeType.ArrayLeafObject:
                    case nodeType.StructLeafObject:
                    case nodeType.StructLeafInt:
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (int.TryParse(proptext.Text, out i))
                        {
                            WriteMem(pos, BitConverter.GetBytes(i));
                            UpdateMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafEnum:
                    case nodeType.StructLeafEnum:
                        i = pcc.FindNameOrAdd(propDropdown.SelectedItem as string);
                        WriteMem(pos, BitConverter.GetBytes(i));
                        UpdateMem(pos);
                        break;
                    case nodeType.ArrayLeafName:
                    case nodeType.StructLeafName:
                        if (int.TryParse(proptext.Text, out i))
                        {
                            if (!pcc.Names.Contains(nameEntry.Text) &&
                                DialogResult.No == MessageBox.Show($"{Path.GetFileName(pcc.FileName)} does not contain the Name: {nameEntry.Text}\nWould you like to add it to the Name list?", "", MessageBoxButtons.YesNo))
                            {
                                break;
                            }
                            WriteMem(pos, BitConverter.GetBytes(pcc.FindNameOrAdd(nameEntry.Text)));
                            WriteMem(pos + 4, BitConverter.GetBytes(i));
                            UpdateMem(pos);
                        }
                        break;
                    case nodeType.ArrayLeafString:
                    case nodeType.StructLeafStr:
                        string s = proptext.Text;
                        int offset = pos;
                        int stringMultiplier = 1;
                        int oldLength = BitConverter.ToInt32(memory, offset);
                        if (oldLength < 0)
                        {
                            stringMultiplier = 2;
                            oldLength *= -2;
                        }
                        int oldSize = 4 + oldLength;
                        List<byte> stringBuff = new List<byte>(s.Length * stringMultiplier);
                        if (stringMultiplier == 2)
                        {
                            for (int j = 0; j < s.Length; j++)
                            {
                                stringBuff.AddRange(BitConverter.GetBytes(s[j]));
                            }
                            stringBuff.Add(0);
                        }
                        else
                        {
                            for (int j = 0; j < s.Length; j++)
                            {
                                stringBuff.Add(BitConverter.GetBytes(s[j])[0]);
                            }
                        }
                        stringBuff.Add(0);
                        byte[] buff = BitConverter.GetBytes((s.Count() + 1) * stringMultiplier + 4);
                        for (int j = 0; j < 4; j++)
                            memory[offset - 8 + j] = buff[j];
                        buff = BitConverter.GetBytes((s.Count() + 1) * (stringMultiplier == 1 ? 1 : -1));
                        for (int j = 0; j < 4; j++)
                            memory[offset + j] = buff[j];
                        buff = new byte[memory.Length - oldLength + stringBuff.Count];
                        int startLength = offset + 4;
                        int startLength2 = startLength + oldLength;
                        for (int j = 0; j < startLength; j++)
                        {
                            buff[j] = memory[j];
                        }
                        for (int j = 0; j < stringBuff.Count; j++)
                        {
                            buff[j + startLength] = stringBuff[j];
                        }
                        startLength += stringBuff.Count;
                        for (int j = 0; j < memory.Length - startLength2; j++)
                        {
                            buff[j + startLength] = memory[j + startLength2];
                        }
                        memory = buff;

                        //bubble up size
                        TreeNode parent = LAST_SELECTED_NODE.Parent;
                        while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) ||
                            parent.Tag.Equals(nodeType.ArrayLeafStruct) || isStructLeaf((nodeType)parent.Tag)))
                        {
                            if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct || isStructLeaf((nodeType)parent.Tag))
                            {
                                parent = parent.Parent;
                                continue;
                            }
                            updateArrayLength(getPosFromNode(parent.Name), 0, (stringBuff.Count + 4) - oldSize);
                            parent = parent.Parent;
                        }
                        UpdateMem(pos);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setNonArrayProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int type = BitConverter.ToInt32(memory, pos + 8);
                int test = BitConverter.ToInt32(memory, pos + 12);
                if (test != 0 || !pcc.isName(type))
                    return;
                int i = 0;
                float f = 0;
                byte b = 0;
                switch (pcc.getNameEntry(type))
                {
                    case "IntProperty":
                    case "ObjectProperty":
                    case "StringRefProperty":
                        if (int.TryParse(proptext.Text, out i))
                        {
                            WriteMem(pos + 24, BitConverter.GetBytes(i));
                            UpdateMem(pos);
                        }
                        break;
                    case "NameProperty":
                        if (int.TryParse(proptext.Text, out i))
                        {
                            if (!pcc.Names.Contains(nameEntry.Text) &&
                                DialogResult.No == MessageBox.Show($"{Path.GetFileName(pcc.FileName)} does not contain the Name: {nameEntry.Text}\nWould you like to add it to the Name list?", "", MessageBoxButtons.YesNo))
                            {
                                break;
                            }
                            WriteMem(pos + 24, BitConverter.GetBytes(pcc.FindNameOrAdd(nameEntry.Text)));
                            WriteMem(pos + 28, BitConverter.GetBytes(i));
                            UpdateMem(pos);
                        }
                        break;
                    case "FloatProperty":
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos + 24, BitConverter.GetBytes(f));
                            UpdateMem(pos);
                        }
                        break;
                    case "BoolProperty":
                        memory[pos + 24] = (byte)propDropdown.SelectedIndex;
                        UpdateMem(pos);
                        break;
                    case "ByteProperty":
                        int valOffset;
                        if (pcc.Game == MEGame.ME3)
                        {
                            valOffset = 32;
                        }
                        else
                        {
                            valOffset = 24;
                        }
                        if (propDropdown.Visible)
                        {
                            i = pcc.FindNameOrAdd(propDropdown.SelectedItem as string);
                            WriteMem(pos + valOffset, BitConverter.GetBytes(i));
                            UpdateMem(pos);
                        }
                        else if (byte.TryParse(proptext.Text, out b))
                        {
                            memory[pos + valOffset] = b;
                            UpdateMem(pos);
                        }
                        break;
                    case "StrProperty":
                        string s = proptext.Text;
                        int offset = pos + 24;
                        int stringMultiplier = 1;
                        int oldSize = BitConverter.ToInt32(memory, pos + 16);
                        int oldLength = BitConverter.ToInt32(memory, offset);
                        if (oldLength < 0)
                        {
                            stringMultiplier = 2;
                            oldLength *= -2;
                        }
                        List<byte> stringBuff = new List<byte>(s.Length * stringMultiplier);
                        if (stringMultiplier == 2)
                        {
                            for (int j = 0; j < s.Length; j++)
                            {
                                stringBuff.AddRange(BitConverter.GetBytes(s[j]));
                            }
                            stringBuff.Add(0);
                        }
                        else
                        {
                            for (int j = 0; j < s.Length; j++)
                            {
                                stringBuff.Add(BitConverter.GetBytes(s[j])[0]);
                            }
                        }
                        stringBuff.Add(0);
                        byte[] buff = BitConverter.GetBytes((s.Count() + 1) * stringMultiplier + 4);
                        for (int j = 0; j < 4; j++)
                            memory[offset - 8 + j] = buff[j];
                        buff = BitConverter.GetBytes((s.Count() + 1) * (stringMultiplier == 1 ? 1 : -1));
                        for (int j = 0; j < 4; j++)
                            memory[offset + j] = buff[j];
                        buff = new byte[memory.Length - oldLength + stringBuff.Count];
                        int startLength = offset + 4;
                        int startLength2 = startLength + oldLength;
                        for (int j = 0; j < startLength; j++)
                        {
                            buff[j] = memory[j];
                        }
                        for (int j = 0; j < stringBuff.Count; j++)
                        {
                            buff[j + startLength] = stringBuff[j];
                        }
                        startLength += stringBuff.Count;
                        for (int j = 0; j < memory.Length - startLength2; j++)
                        {
                            buff[j + startLength] = memory[j + startLength2];
                        }
                        memory = buff;

                        //bubble up size
                        TreeNode parent = LAST_SELECTED_NODE.Parent;
                        while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) || parent.Tag.Equals(nodeType.ArrayLeafStruct)))
                        {
                            if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct)
                            {
                                parent = parent.Parent;
                                continue;
                            }
                            updateArrayLength(getPosFromNode(parent.Name), 0, (stringBuff.Count + 4) - oldSize);
                            parent = parent.Parent;
                        }
                        UpdateMem(pos);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void deleteElement()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (hb1.SelectionStart != lastSetOffset)
                {
                    return; //user manually moved cursor
                }


                int size; //num bytes to delete at pos
                switch (className)
                {
                    case "Level":
                        size = 4;
                        int offset = getPosFromNode(LAST_SELECTED_NODE.Name);
                        int start = 0x4;
                        while (start < export.Data.Length)
                        {
                            uint nameindex = BitConverter.ToUInt32(export.Data, start);
                            if (nameindex < pcc.Names.Count && pcc.Names[(int)nameindex] == "None")
                            {
                                //found it
                                start += 8;
                                break;
                            }
                            else
                            {
                                start += 4;
                            }
                        }

                        //Console.WriteLine("Found start of binary at " + start.ToString("X8"));

                        uint exportid = BitConverter.ToUInt32(export.Data, start);
                        start += 4;
                        uint numberofitems = BitConverter.ToUInt32(export.Data, start);
                        numberofitems--;
                        WriteMem(start, BitConverter.GetBytes(numberofitems));
                        //Debug.WriteLine("Size before: " + memory.Length);
                        memory = RemoveIndices(memory, offset, size);
                        //Debug.WriteLine("Size after: " + memory.Length);

                        UpdateMem();
                        RefreshMem();
                        break;
                    case "Class":
                        size = 4;
                        break;


                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }

        private void addArrayLeaf()
        {

            int pos = (int)hb1.SelectionStart;
            if (hb1.SelectionStart != lastSetOffset)
            {
                return; //user manually moved cursor
            }

            try
            {
                //int size; //num bytes to delete at pos
                switch (className)
                {
                    case "Level":
                        int i = -1;
                        if (!int.TryParse(proptext.Text, out i))
                        {
                            return; //not valid element
                        }
                        int start = 0x4;
                        while (start < export.Data.Length)
                        {
                            uint nameindex = BitConverter.ToUInt32(export.Data, start);
                            if (nameindex < pcc.Names.Count && pcc.Names[(int)nameindex] == "None")
                            {
                                //found it
                                start += 8;
                                break;
                            }
                            else
                            {
                                start += 4;
                            }
                        }

                        //Console.WriteLine("Found start of binary at " + start.ToString("X8"));

                        uint exportid = BitConverter.ToUInt32(export.Data, start);
                        start += 4;
                        uint numberofitems = BitConverter.ToUInt32(export.Data, start);
                        numberofitems++;
                        WriteMem(start, BitConverter.GetBytes(numberofitems));
                        //Debug.WriteLine("Size before: " + memory.Length);
                        //memory = RemoveIndices(memory, offset, size);
                        int offset = (int)(start + numberofitems * 4); //will be at the very end of the list as it is now +1
                        List<byte> memList = memory.ToList();
                        memList.InsertRange(offset, BitConverter.GetBytes(i));
                        memory = memList.ToArray();
                        //export.Data = memory.TypedClone();
                        UpdateMem(offset);
                        break;
                    case "Class":
                        //size = 4;
                        break;
                }
                RefreshMem();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }

        private T[] RemoveIndices<T>(T[] IndicesArray, int RemoveAt, int NumElementsToRemove)
        {
            if (RemoveAt < 0 || RemoveAt > IndicesArray.Length - 1 || NumElementsToRemove < 0 || NumElementsToRemove + RemoveAt > IndicesArray.Length - 1)
            {
                return IndicesArray;
            }
            T[] newIndicesArray = new T[IndicesArray.Length - NumElementsToRemove];

            int i = 0;
            int j = 0;
            while (i < IndicesArray.Length)
            {
                if (i < RemoveAt || i >= RemoveAt + NumElementsToRemove)
                {
                    newIndicesArray[j] = IndicesArray[i];
                    j++;
                }
                else
                {
                    //Debug.WriteLine("Skipping byte: " + i.ToString("X4"));
                }

                i++;
            }

            return newIndicesArray;
        }

        private void WriteMem(int pos, byte[] buff)
        {
            for (int i = 0; i < buff.Length; i++)
                memory[pos + i] = buff[i];
        }

        /// <summary>
        /// Updates an array properties length and size in bytes. Does not refresh the memory view
        /// </summary>
        /// <param name="startpos">Starting index of the array property</param>
        /// <param name="countDelta">Delta in terms of how many items the array has</param>
        /// <param name="byteDelta">Delta in terms of how many bytes the array data is</param>
        private void updateArrayLength(int startpos, int countDelta, int byteDelta)
        {
            int sizeOffset = 16;
            int countOffset = 24;
            int oldSize = BitConverter.ToInt32(memory, sizeOffset + startpos);
            int oldCount = BitConverter.ToInt32(memory, countOffset + startpos);

            int newSize = oldSize + byteDelta;
            int newCount = oldCount + countDelta;

            WriteMem(startpos + sizeOffset, BitConverter.GetBytes(newSize));
            WriteMem(startpos + countOffset, BitConverter.GetBytes(newCount));

        }


        private void UpdateMem(int? _selectedNodePos = null)
        {
            export.Data = memory.TypedClone();
            selectedNodePos = _selectedNodePos;
        }

        public void RefreshMem()
        {
            hb1.ByteProvider = new DynamicByteProvider(memory);
            //adds rootnode to list
            List<TreeNode> allNodes = treeView1.Nodes.Cast<TreeNode>().ToList();
            //flatten tree of nodes into list.
            for (int i = 0; i < allNodes.Count(); i++)
            {
                allNodes.AddRange(allNodes[i].Nodes.Cast<TreeNode>());
            }

            var expandedNodes = allNodes.Where(x => x.IsExpanded).Select(x => x.Name);
            StartScan(treeView1.TopNode?.Name, selectedNodePos?.ToString());

        }

        private string CheckSeperator(string s)
        {
            string seperator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            string wrongsep;
            if (seperator == ".")
                wrongsep = ",";
            else
                wrongsep = ".";
            return s.Replace(wrongsep, seperator);
        }

        private void expandAllButton_Click(object sender, EventArgs e)
        {
            if (treeView1 != null)
            {
                treeView1.ExpandAll();
            }
        }

        private void collapseAllButton_Click(object sender, EventArgs e)
        {
            if (treeView1 != null)

            {
                treeView1.CollapseAll();
                treeView1.Nodes[0].Expand();
            }
        }

        private void deleteElement_Click(object sender, EventArgs e)
        {
            deleteElement();
        }

        private void addArrayElementButton_Click(object sender, EventArgs e)
        {
            addArrayLeaf();
        }

        private void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag != null && e.Node.Tag.Equals(nodeType.ArrayProperty) && e.Node.Nodes.Count == 1)
            {
                e.Node.Nodes[0].Expand();
            }
        }

        private void proptext_KeyUp(object sender, KeyEventArgs e)
        {
            if (objectNameLabel.Visible)
            {
                int i;
                if (int.TryParse(proptext.Text, out i))
                {
                    objectNameLabel.Text = $"({pcc.getObjectName(i)})";
                }
                else
                {
                    objectNameLabel.Text = "()";
                }
            }
        }

        private void moveUpButton_Click(object sender, EventArgs e)
        {
            moveElement(true);
        }

        private void moveDownButton_Click(object sender, EventArgs e)
        {
            moveElement(false);
        }

        private void moveElement(bool up)
        {
            if (hb1.SelectionStart != lastSetOffset)
            {
                return;//user manually moved cursor
            }
            int pos;
            TreeNode node;
            TreeNode parent = LAST_SELECTED_NODE.Parent;
            if (up)
            {
                node = LAST_SELECTED_NODE.PrevNode;
                pos = getPosFromNode(node.Name);
            }
            else
            {
                node = LAST_SELECTED_NODE.NextNode;
                pos = getPosFromNode(node.Name);
                //account for structs not neccesarily being the same size
                if (node.Nodes.Count > 0)
                {
                    //position of element being moved down + size of struct below it
                    pos = lastSetOffset + (getPosFromNode(node.LastNode.Name) + 8 - pos);
                }
            }
            byte[] element = new byte[0]; //PLACEHOLDER!
            List<byte> memList = memory.ToList();
            memList.InsertRange(pos, element);
            memory = memList.ToArray();
            //bubble up size
            bool firstbubble = true;
            int parentOffset;
            while (parent != null && (parent.Tag.Equals(nodeType.StructProperty) || parent.Tag.Equals(nodeType.ArrayProperty) || parent.Tag.Equals(nodeType.ArrayLeafStruct)))
            {
                if ((nodeType)parent.Tag == nodeType.ArrayLeafStruct)
                {
                    parent = parent.Parent;
                    continue;
                }
                parentOffset = getPosFromNode(parent.Name);
                if (firstbubble)
                {
                    firstbubble = false;
                    updateArrayLength(parentOffset, 1, element.Length);
                }
                else
                {
                    updateArrayLength(parentOffset, 0, element.Length);
                }
                parent = parent.Parent;
            }
            if (node.Nodes.Count > 0)
            {
                UpdateMem(-pos);
            }
            else
            {
                UpdateMem(pos);
            }
        }

        private void addPropButton_Click(object sender, EventArgs e)
        {
            List<string> props = PropertyReader.getPropList(export).Select(x => pcc.getNameEntry(x.Name)).ToList();
            string prop = AddPropertyDialog.GetProperty(export, props, pcc.Game);
            if (prop != null)
            {
                PropertyInfo info = GetPropertyInfo(prop, className);
                if (info.type == PropertyType.StructProperty && pcc.Game != MEGame.ME3)
                {
                    MessageBox.Show("Cannot add StructProperties when editing ME1 or ME2 files.", "Sorry :(");
                    return;
                }
                List<byte> buff = new List<byte>();
                //name
                buff.AddRange(BitConverter.GetBytes(pcc.FindNameOrAdd(prop)));
                buff.AddRange(new byte[4]);
                //type
                buff.AddRange(BitConverter.GetBytes(pcc.FindNameOrAdd(info.type.ToString())));
                buff.AddRange(new byte[4]);

                switch (info.type)
                {
                    case PropertyType.IntProperty:
                    case PropertyType.StringRefProperty:
                    case PropertyType.FloatProperty:
                    case PropertyType.ObjectProperty:
                    case PropertyType.ArrayProperty:
                        //size
                        buff.AddRange(BitConverter.GetBytes(4));
                        buff.AddRange(new byte[4]);
                        //value
                        buff.AddRange(BitConverter.GetBytes(0));
                        break;
                    case PropertyType.NameProperty:
                        //size
                        buff.AddRange(BitConverter.GetBytes(8));
                        buff.AddRange(new byte[4]);
                        //value
                        buff.AddRange(BitConverter.GetBytes(pcc.FindNameOrAdd("None")));
                        buff.AddRange(BitConverter.GetBytes(0));
                        break;
                    case PropertyType.BoolProperty:
                        //size
                        buff.AddRange(BitConverter.GetBytes(0));
                        buff.AddRange(new byte[4]);
                        //value
                        if (pcc.Game == MEGame.ME3)
                        {
                            buff.Add(0);
                        }
                        else
                        {
                            buff.AddRange(new byte[4]);
                        }
                        break;
                    case PropertyType.StrProperty:
                        //size
                        buff.AddRange(BitConverter.GetBytes(6));
                        buff.AddRange(new byte[4]);
                        //value
                        if (pcc.Game == MEGame.ME3)
                        {
                            buff.AddRange(BitConverter.GetBytes(-1));
                            buff.Add(0);
                        }
                        else
                        {
                            buff.AddRange(BitConverter.GetBytes(1));
                        }
                        buff.Add(0);
                        break;
                    case PropertyType.DelegateProperty:
                        //size
                        buff.AddRange(BitConverter.GetBytes(12));
                        buff.AddRange(new byte[4]);
                        //value
                        buff.AddRange(BitConverter.GetBytes(0));
                        buff.AddRange(BitConverter.GetBytes(0));
                        buff.AddRange(BitConverter.GetBytes(0));
                        break;
                    case PropertyType.ByteProperty:
                        if (info.reference == null)
                        {
                            //size
                            buff.AddRange(BitConverter.GetBytes(1));
                            buff.AddRange(new byte[4]);
                            if (pcc.Game == MEGame.ME3)
                            {
                                //enum Type
                                buff.AddRange(BitConverter.GetBytes(pcc.FindNameOrAdd("None")));
                                buff.AddRange(new byte[4]);
                            }
                            //value
                            buff.Add(0);
                        }
                        else
                        {
                            //size
                            buff.AddRange(BitConverter.GetBytes(8));
                            buff.AddRange(new byte[4]);
                            if (pcc.Game == MEGame.ME3)
                            {
                                //enum Type
                                buff.AddRange(BitConverter.GetBytes(pcc.FindNameOrAdd(info.reference)));
                                buff.AddRange(new byte[4]);
                            }
                            //value
                            buff.AddRange(BitConverter.GetBytes(pcc.FindNameOrAdd("None")));
                            buff.AddRange(new byte[4]);
                        }
                        break;
                    case PropertyType.StructProperty:
                        byte[] structBuff = ME3UnrealObjectInfo.getDefaultClassValue(pcc as ME3Package, info.reference);
                        if (structBuff == null)
                        {
                            return;
                        }
                        //size
                        buff.AddRange(BitConverter.GetBytes(structBuff.Length));
                        buff.AddRange(new byte[4]);
                        //struct Type
                        buff.AddRange(BitConverter.GetBytes(pcc.FindNameOrAdd(info.reference)));
                        buff.AddRange(new byte[4]);
                        //value
                        buff.AddRange(structBuff);
                        break;
                    default:
                        return;
                }
                int pos = getPosFromNode(treeView1.Nodes[0].LastNode.Name);
                List<byte> memlist = memory.ToList();
                memlist.InsertRange(pos, buff);
                memory = memlist.ToArray();
                UpdateMem(pos);
            }
        }

        private void splitContainer1_SplitterMoving(object sender, SplitterCancelEventArgs e)
        {
            //a hack to set max width for SplitContainer1
            if (splitContainer1.Width - HEXBOX_MAX_WIDTH > 0)
            {
                splitContainer1.Panel2MinSize = splitContainer1.Width - HEXBOX_MAX_WIDTH;
            }
        }

        private void toggleHexWidthButton_Click(object sender, EventArgs e)
        {
            if (splitContainer1.SplitterDistance > splitContainer1.Panel1MinSize)
            {
                splitContainer1.SplitterDistance = splitContainer1.Panel1MinSize;
            }
            else
            {
                splitContainer1.SplitterDistance = HEXBOX_MAX_WIDTH;
            }
        }

        private void hb1_SelectionChanged(object sender, EventArgs e)
        {
            int start = (int)hb1.SelectionStart;
            int len = (int)hb1.SelectionLength;
            int size = (int)hb1.ByteProvider.Length;
            try
            {
                if (memory != null && start != -1 && start + len < size)
                {
                    string s = $"Byte: {memory[start]}";
                    if (start <= memory.Length - 4)
                    {
                        s += $", Int: {BitConverter.ToInt32(memory, start)}";
                    }
                    s += $" | Start=0x{start.ToString("X8")} ";
                    if (len > 0)
                    {
                        s += $"Length=0x{len.ToString("X8")} ";
                        s += $"End=0x{(start + len - 1).ToString("X8")}";
                    }
                    selectionStatus.Text = s;
                }
                else
                {
                    selectionStatus.Text = "Nothing Selected";
                }
            }
            catch (Exception)
            {
            }
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = e.Node;
                if (e.Node.Nodes.Count != 0)
                {
                    nodeContextMenuStrip1.Show(MousePosition);
                }
            }
        }

        private void expandAllChildrenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1.SelectedNode.ExpandAll();
        }

        private void collapseAllChildrenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1.SelectedNode.Collapse(false);
        }

        private int getPosFromNode(TreeNode t)
        {
            return getPosFromNode(t.Name);
        }

        private int getPosFromNode(string s)
        {
            return Math.Abs(Convert.ToInt32(s));
        }

        #region UnrealObjectInfo
        private PropertyInfo GetPropertyInfo(int propName)
        {
            switch (pcc.Game)
            {
                case MEGame.ME1:
                    return ME1UnrealObjectInfo.getPropertyInfo(className, pcc.getNameEntry(propName));
                case MEGame.ME2:
                    return ME2UnrealObjectInfo.getPropertyInfo(className, pcc.getNameEntry(propName));
                case MEGame.ME3:
                    return ME3UnrealObjectInfo.getPropertyInfo(className, pcc.getNameEntry(propName));
            }
            return null;
        }

        private PropertyInfo GetPropertyInfo(string propname, string typeName, bool inStruct = false)
        {
            switch (pcc.Game)
            {
                case MEGame.ME1:
                    return ME1UnrealObjectInfo.getPropertyInfo(typeName, propname, inStruct);
                case MEGame.ME2:
                    return ME2UnrealObjectInfo.getPropertyInfo(typeName, propname, inStruct);
                case MEGame.ME3:
                    return ME3UnrealObjectInfo.getPropertyInfo(typeName, propname, inStruct);
            }
            return null;
        }

        private ArrayType GetArrayType(PropertyInfo propInfo)
        {
            switch (pcc.Game)
            {
                case MEGame.ME1:
                    return ME1UnrealObjectInfo.getArrayType(propInfo);
                case MEGame.ME2:
                    return ME2UnrealObjectInfo.getArrayType(propInfo);
                case MEGame.ME3:
                    return ME3UnrealObjectInfo.getArrayType(propInfo);
            }
            return ArrayType.Int;
        }

        private ArrayType GetArrayType(int propName, string typeName = null)
        {
            if (typeName == null)
            {
                typeName = className;
            }
            switch (pcc.Game)
            {
                case MEGame.ME1:
                    return ME1UnrealObjectInfo.getArrayType(typeName, pcc.getNameEntry(propName));
                case MEGame.ME2:
                    return ME2UnrealObjectInfo.getArrayType(typeName, pcc.getNameEntry(propName));
                case MEGame.ME3:
                    return ME3UnrealObjectInfo.getArrayType(typeName, pcc.getNameEntry(propName));
            }
            return ArrayType.Int;
        }

        private List<string> GetEnumValues(string enumName, int propName)
        {
            switch (pcc.Game)
            {
                case MEGame.ME1:
                    return ME1UnrealObjectInfo.getEnumfromProp(className, pcc.getNameEntry(propName));
                case MEGame.ME2:
                    return ME2UnrealObjectInfo.getEnumfromProp(className, pcc.getNameEntry(propName));
                case MEGame.ME3:
                    return ME3UnrealObjectInfo.getEnumValues(enumName, true);
            }
            return null;
        }
        #endregion

        private void FindButton_Click(object sender, EventArgs e)
        {
            TreeNodeCollection collect = treeView1.Nodes;
            if (collect.Count > 0)
            {
                collect = collect[0].Nodes;
            }
            string searchtext = findBox.Text;

            foreach (TreeNode node in collect)
            {
                if (node.Text.Contains(searchtext))
                {
                    treeView1.SelectedNode = node;
                    break;
                }
            }
        }

        private void findButton_Pressed(object sender, KeyPressEventArgs e)
        {
            if (this.findBox.Focused && e.KeyChar == '\r')
            {
                // click the Go button
                this.findButton.PerformClick();
                // don't allow the Enter key to pass to textbox
                e.Handled = true;
            }

        }

        private void viewModeChanged(object sender, EventArgs e)
        {
            InterpreterMode = ((ToolStripComboBox)sender).SelectedIndex;
            if (memory != null)
            {
                RefreshMem();
            }
        }

        private void saveHexButton_Click(object sender, EventArgs e)
        {

        }
    }
}
