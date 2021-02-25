﻿#if DEBUG
//#define DEBUGSCRIPT
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ME3ExplorerCore;
using ME3ExplorerCore.GameFilesystem;
using ME3ExplorerCore.Packages;
using ME3ExplorerCore.Unreal.BinaryConverters;
using ME3Script.Analysis.Symbols;
using ME3Script.Analysis.Visitors;
using ME3Script.Compiling.Errors;
using ME3Script.Decompiling;
using ME3Script.Language.Tree;
using ME3ExplorerCore.Helpers;
using ME3Script.Lexing;
using ME3Script.Parsing;

namespace ME3Script
{

    public static class StandardLibrary
    {
        private static SymbolTable _symbols;
        public static SymbolTable GetSymbolTable() => IsInitialized ? _symbols?.Clone() : null;

        public static SymbolTable ReadonlySymbolTable => IsInitialized ? _symbols : null;

        //public static readonly CaseInsensitiveDictionary<(Class ast, string scriptText)> Classes = new CaseInsensitiveDictionary<(Class ast, string scriptText)>();

        public static bool IsInitialized { get; private set; }

        public static bool HadInitializationError { get; private set; }

        public static event EventHandler Initialized;

        private static readonly object initializationLock = new();

        public static async Task<bool> InitializeStandardLib(params string[] additionalFiles)
        {
            if (IsInitialized)
            {
                return true;
            }

            return await Task.Run(() =>
            {
                bool success;
                if (IsInitialized)
                {
                    return true;
                }
                lock (initializationLock)
                {
                    if (IsInitialized)
                    {
                        return true;
                    }
                    success = InternalInitialize(additionalFiles);
                    IsInitialized = success;
                    HadInitializationError = !success;
                }
                Initialized?.Invoke(null, EventArgs.Empty);
                return success;
            });
        }

        private static bool InternalInitialize(params string[] additionalFiles)
        {
            try
            {
#if AZURE
                var filePaths = new[] { "Core.pcc", "Engine.pcc", "GameFramework.pcc", "GFxUI.pcc", "WwiseAudio.pcc", "SFXOnlineFoundation.pcc" }
                    .Select(f => Path.Combine(ME3Directory.CookedPCPath, f)).ToList();
#else 
                var filePaths = new[] { "Core.pcc", "Engine.pcc", "GameFramework.pcc", "GFxUI.pcc", "WwiseAudio.pcc", "SFXOnlineFoundation.pcc", "SFXGame.pcc" }
                    .Select(f => Path.Combine(ME3Directory.CookedPCPath, f)).ToList();
#endif
                filePaths.AddRange(additionalFiles);
                if (!filePaths.All(File.Exists))
                {
                    return false;
                }
                using var files = MEPackageHandler.OpenMEPackages(filePaths);

                return files.All(pcc => ResolveAllClassesInPackage(pcc, ref _symbols));
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public static bool ResolveAllClassesInPackage(IMEPackage pcc, ref SymbolTable symbols)
        {
            string fileName = Path.GetFileNameWithoutExtension(pcc.FilePath);
#if DEBUGSCRIPT
            string dumpFolderPath = Path.Combine(MEDirectories.GetDefaultGamePath(pcc.Game), "ScriptDump", fileName);
            Directory.CreateDirectory(dumpFolderPath);
#endif
            var log = new MessageLog();
            Debug.WriteLine($"{fileName}: Beginning Parse.");
            var classes = new List<(Class ast, string scriptText)>();
            foreach (ExportEntry export in pcc.Exports.Where(exp => exp.IsClass))
            {
                Class cls = ScriptObjectToASTConverter.ConvertClass(export.GetBinaryData<UClass>(), false);
                if (!cls.IsFullyDefined)
                {
                    continue;
                }
                string scriptText = "";
                try
                {
#if DEBUGSCRIPT
                    var codeBuilder = new CodeBuilderVisitor();
                    cls.AcceptVisitor(codeBuilder);
                    scriptText = codeBuilder.GetOutput();
                    File.WriteAllText(Path.Combine(dumpFolderPath, $"{cls.Name}.uc"), scriptText);
                    var parser = new ClassOutlineParser(new TokenStream<string>(new StringLexer(scriptText, log)), log);
                    cls = parser.TryParseClass();
                    if (cls == null || log.Content.Any())
                    {
                        DisplayError(scriptText, log.ToString());
                        return false;
                    }
#endif

                    if (export.ObjectName == "Object")
                    {
                        symbols = SymbolTable.CreateIntrinsicTable(cls);
                    }
                    else
                    {
                        symbols.AddType(cls);
                    }

                    classes.Add(cls, scriptText);
                }
                catch (Exception e) when (!ME3ExplorerCoreLib.IsDebug)
                {
                    DisplayError(scriptText, log.ToString());
                    return false;
                }
            }
            Debug.WriteLine($"{fileName}: Finished parse.");
            foreach (var validationPass in Enums.GetValues<ValidationPass>())
            {
                foreach ((Class ast, string scriptText) in classes)
                {
                    try
                    {
                        var validator = new ClassValidationVisitor(log, symbols, validationPass);
                        ast.AcceptVisitor(validator);
                        if (log.Content.Any())
                        {
                            DisplayError(scriptText, log.ToString());
                            return false;
                        }
                    }
                    catch (Exception e) when(!ME3ExplorerCoreLib.IsDebug)
                    {
                        DisplayError(scriptText, log.ToString());
                        return false;
                    }
                }
                Debug.WriteLine($"{fileName}: Finished validation pass {validationPass}.");
            }

            switch (fileName)
            {
                case "Core":
                    symbols.InitializeOperators();
                    break;
                case "Engine":
                    symbols.ValidateIntrinsics();
                    break;
            }

#if DEBUGSCRIPT
            //parse function bodies for testing purposes
            foreach ((Class ast, string scriptText) in classes)
            {
                symbols.RevertToObjectStack();
                if (!ast.Name.CaseInsensitiveEquals("Object"))
                {
                    symbols.GoDirectlyToStack(((Class)ast.Parent).GetInheritanceString());
                    symbols.PushScope(ast.Name);
                }

                foreach (Function function in ast.Functions.Where(func => !func.IsNative && func.IsDefined))
                {
                    CodeBodyParser.ParseFunction(function, pcc.Game, scriptText, symbols, log);
                    if (log.Content.Any())
                    {
                        DisplayError(scriptText, log.ToString());
                    }
                }
            }
#endif


            symbols.RevertToObjectStack();
            
            return true;
        }


        [Conditional("DEBUGSCRIPT")]
        static void DisplayError(string scriptText, string logText)
        {
            string scriptFile = Path.Combine("TEMPME3Script.txt");
            string logFile = Path.Combine("TEMPME3Script.log");
            File.WriteAllText(scriptFile, scriptText);
            File.WriteAllText(logFile, logText);
            Process.Start("notepad++", $"\"{scriptFile}\" \"{logFile}\"");
        }
    }
}