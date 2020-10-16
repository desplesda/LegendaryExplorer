using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ME3ExplorerCore.Packages;
using ME3ExplorerCore.Tests.helpers;
using ME3ExplorerCore.Unreal;
using ME3ExplorerCore.Unreal.BinaryConverters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ME3ExplorerCore.Tests
{
    [TestClass]
    public class PackageTests
    {
        [TestMethod]
        public void TestPackages()
        {
            GlobalTest.Init();
            // Loads compressed packages and attempts to enumerate every object's properties.
            var packagesPath = GlobalTest.GetTestPackagesDirectory();
            var packages = Directory.GetFiles(packagesPath, "*.*", SearchOption.AllDirectories);
            foreach (var p in packages)
            {
                if (p.RepresentsPackageFilePath())
                {
                    // Do not use package caching in tests
                    Console.WriteLine($"Opening package {p}");

                    (MEGame expectedGame, MEPackage.GamePlatform expectedPlatform) = GlobalTest.GetExpectedTypes(p);

                    var package = MEPackageHandler.OpenMEPackage(p, forceLoadFromDisk: true);

                    Assert.AreEqual(expectedGame, package.Game,
                        "The expected game and the resolved game do not match!");
                    Assert.AreEqual(expectedPlatform, package.Platform,
                        "The expected platform and the resolved platform do not match!");
                    Console.WriteLine($" > Enumerating all exports for properties");

                    foreach (var exp in package.Exports)
                    {
                        if (exp.ClassName != "Class")
                        {
                            var props = exp.GetProperties(forceReload: true, includeNoneProperties: true);
                            Assert.IsInstanceOfType(props.LastOrDefault(), typeof(NoneProperty),
                                $"Error parsing properties on export {exp.UIndex} {exp.InstancedFullPath} in file {exp.FileRef.FilePath}");
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void TestCompression()
        {
            GlobalTest.Init();

            // Loads compressed packages, save them uncompressed. Load package, save re-compressed, compare results
            var packagesPath = GlobalTest.GetTestPackagesDirectory();
            //var packages = Directory.GetFiles(packagesPath, "*.*", SearchOption.AllDirectories);
            var packages = Directory.GetFiles(packagesPath, "*.*", SearchOption.AllDirectories);
            foreach (var p in packages)
            {
                if (p.RepresentsPackageFilePath())
                {
                    // Do not use package caching in tests
                    Console.WriteLine($"Opening package {p}");
                    var originalLoadedPackage = MEPackageHandler.OpenMEPackage(p, forceLoadFromDisk: true);
                    if (originalLoadedPackage.Platform != MEPackage.GamePlatform.PC)
                    {
                        Assert.ThrowsException<Exception>(() => { originalLoadedPackage.SaveToStream(true); },
                            "Non-PC platform package should not be saveable. An exception should have been thrown to stop this!");
                        continue;
                    }

                    // Is PC
                    var uncompressedPS = originalLoadedPackage.SaveToStream(false);
                    var compressedPS = originalLoadedPackage.SaveToStream(true);


                    uncompressedPS.Position = compressedPS.Position = 0;

                    var reopenedUCP = MEPackageHandler.OpenMEPackageFromStream(uncompressedPS);
                    var reopenedCCP = MEPackageHandler.OpenMEPackageFromStream(compressedPS);

                    Assert.AreEqual(reopenedCCP.NameCount, reopenedUCP.NameCount,
                        $"Name count is not identical between compressed/uncompressed packages");
                    Assert.AreEqual(reopenedCCP.ImportCount, reopenedUCP.ImportCount,
                        $"Import count is not identical between compressed/uncompressed packages");
                    Assert.AreEqual(reopenedCCP.ExportCount, reopenedUCP.ExportCount,
                        $"Export count is not identical between compressed/uncompressed packages");

                    for (int i = 0; i < reopenedCCP.NameCount; i++)
                    {
                        var nameCCP = reopenedCCP.Names[i];
                        var nameUCP = reopenedUCP.Names[i];
                        Assert.AreEqual(nameCCP, nameUCP,
                            $"Names are not identical between compressed/uncompressed packages, name index {i}");
                    }

                    for (int i = 0; i < reopenedCCP.ImportCount; i++)
                    {
                        var importCCP = reopenedCCP.Imports[i];
                        var importUCP = reopenedUCP.Imports[i];
                        Assert.IsTrue(importCCP.Header.SequenceEqual(importUCP.Header),
                            $"Header data for import {-(i + 1)} are not identical between compressed/uncompressed packages");
                    }

                    for (int i = 0; i < reopenedCCP.ExportCount; i++)
                    {
                        var exportCCP = reopenedCCP.Exports[i];
                        var exportUCP = reopenedUCP.Exports[i];
                        Assert.IsTrue(exportCCP.Header.SequenceEqual(exportUCP.Header),
                            $"Header data for xport {i + 1} are not identical between compressed/uncompressed packages");
                    }
                }
            }
        }

        [TestMethod]
        public void TestBinaryConverters()
        {
            GlobalTest.Init();

            // Loads compressed packages, save them uncompressed. Load package, save re-compressed, compare results
            var packagesPath = GlobalTest.GetTestPackagesDirectory();
            //var packages = Directory.GetFiles(packagesPath, "*.*", SearchOption.AllDirectories);
            var packages = Directory.GetFiles(packagesPath, "*.*", SearchOption.AllDirectories);
            foreach (var p in packages)
            {
                if (p.RepresentsPackageFilePath())
                {
                    // Do not use package caching in tests
                    Console.WriteLine($"Opening package {p}");
                    (var game, var platform) = GlobalTest.GetExpectedTypes(p);
                    if (platform == MEPackage.GamePlatform.PC) // Will expand in future, but not now.
                    {
                        var originalLoadedPackage = MEPackageHandler.OpenMEPackage(p, forceLoadFromDisk: true);
                        foreach (var export in originalLoadedPackage.Exports)
                        {
                            PropertyCollection props = export.GetProperties();
                            ObjectBinary bin = ObjectBinary.From(export) ?? export.GetBinaryData();

                            if (game == MEGame.UDK)
                                continue; // No point testing converting things to UDK in this fashion

                            byte[] original = export.Data;
                            export.WriteProperties(props);
                            export.SetBinaryData(bin);
                            byte[] changed = export.Data;
                            Assert.AreEqual(original.Length, changed.Length,
                                $"Reserialization of export {export.UIndex} {export.InstancedFullPath} produced a different sized byte array than the input. Original size: {original.Length}, reserialized: {changed.Length}, difference: 0x{(changed.Length - original.Length):X8} bytes. File: {p}");
                            Assert.IsTrue(original.SequenceEqual(changed),
                                $"Reserialization of export {export.UIndex} {export.InstancedFullPath} produced a different byte array than the input. File: {p}");
                        }
                    }
                }
            }
        }

        public static string RandomString(Random random, int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        [TestMethod]
        public void TestNameOperations()
        {
            GlobalTest.Init();
            Random random = new Random();
            // Loads compressed packages, save them uncompressed. Load package, save re-compressed, compare results
            var packagesPath = GlobalTest.GetTestPackagesDirectory();
            //var packages = Directory.GetFiles(packagesPath, "*.*", SearchOption.AllDirectories);
            var packages = Directory.GetFiles(packagesPath, "*.*", SearchOption.AllDirectories);
            foreach (var p in packages)
            {
                if (p.RepresentsPackageFilePath())
                {
                    // Do not use package caching in tests
                    Console.WriteLine($"Opening package {p}");
                    (var game, var platform) = GlobalTest.GetExpectedTypes(p);
                    if (platform == MEPackage.GamePlatform.PC) // Will expand in future, but not now.
                    {
                        var loadedPackage = MEPackageHandler.OpenMEPackage(p, forceLoadFromDisk: true);
                        var afterLoadNameCount = loadedPackage.NameCount;
                        for (int i = 0; i < afterLoadNameCount; i++)
                        {
                            var existingName = loadedPackage.Names[i];
                            var existingNameIndex = loadedPackage.FindNameOrAdd(existingName);
                            Assert.IsTrue(existingNameIndex == i,
                                "An existing name was added when it shouldn't have been!");
                        }

                        // Test adding
                        for (int i = 0; i < 20; i++)
                        {
                            var expectedNameIndex = loadedPackage.NameCount;
                            var newName =
                                RandomString(random,
                                    35); // If we have a same-collision on 35 char random strings, let Mgamerz know he should buy a lottery ticket
                            var newNameIndex = loadedPackage.FindNameOrAdd(newName);
                            Assert.AreEqual(expectedNameIndex, newNameIndex,
                                "A name was added, but the index lookup was wrong!");
                        }

                        // Test changing
                        for (int i = 0; i < 20; i++)
                        {
                            var existingIndex = random.Next(loadedPackage.NameCount);
                            var newName = RandomString(random, 38); //even more entropy
                            loadedPackage.replaceName(existingIndex, newName);

                            // Check it's correct
                            var calculatedIndex = loadedPackage.FindNameOrAdd(newName);
                            Assert.AreEqual(existingIndex, calculatedIndex,
                                "A name was replaced, but the index of the replaced name was wrong when looked up via FindNameOrAdd()!");

                            var checkedNameGet = loadedPackage.GetNameEntry(calculatedIndex);
                            var checkedNameAccessor = loadedPackage.GetNameEntry(calculatedIndex);
                            Assert.AreEqual(newName, checkedNameGet,
                                "A name was replaced, but the GetNameEntry() for the replaced name returned the wrong name!");
                            Assert.AreEqual(newName, checkedNameAccessor,
                                "A name was replaced, but the Names[] array accessor for the replaced name returned the wrong name!");
                        }
                    }

                }
            }
        }
    }
}