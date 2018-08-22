﻿using Mono.Cecil;
using Mono.Cecil.Pdb;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Signum.MSBuildTask
{
    public static class Program 
    {
        public static int Main(string[] args)
        {
            //if (args.Length != 2)
            //{
            //    Console.WriteLine("Two arguments are needed: IntermediateAssemblyPath and OutputPath");
            //    Console.WriteLine(@"Example: dotnet &quot;..\Signum.MSBuildTask\Binaries\netcoreapp2.1\Signum.MSBuildTask.dll&quot; &quot;@(IntermediateAssembly)&quot; &quot;$(OutputPath)&quot;");
            //    return -1;
            //}

            string intermediateAssembly = args[0];
            string referencesFile = args[1];
            string[] references = File.ReadAllLines(referencesFile);

            var log = Console.Out;

            if(!File.Exists(intermediateAssembly))
            {
                log.WriteLine("Signum.MSBuildTask skipped (File not found): {0}", intermediateAssembly);
                return 0;
            }

            log.WriteLine("Signum.MSBuildTask starting: {0}", intermediateAssembly);
            try
            {
                var resolver = new PreloadingAssemblyResolver(references);
                bool hasPdb = File.Exists(Path.ChangeExtension(intermediateAssembly, ".pdb"));

                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(intermediateAssembly, new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Deferred,
                    ReadSymbols = hasPdb,
                    InMemory = true,
                    SymbolReaderProvider = hasPdb ? new PdbReaderProvider() : null
                });

                if (AlreadyProcessed(assembly))
                {
                    log.WriteLine("Signum.MSBuildTask already processed: {0}", intermediateAssembly);
                    return 0;
                }

                var errors = new AutoPropertyConverter(assembly, resolver).FixProperties();
                errors |= new FieldAutoInitializer(assembly, resolver, log).FixAutoInitializer();
                errors |= new AutoExpressionField(assembly, resolver, log).FixAutoExpressionField();

                if (errors)
                    return -1;

                MarkAsProcessed(assembly, resolver);

                assembly.Write(intermediateAssembly, new WriterParameters
                {
                    WriteSymbols = hasPdb,
                    SymbolWriterProvider = hasPdb ? new PdbWriterProvider() : null
                });
                
                return 0;
            }
            catch (Exception e)
            {
                log.Write("Exception in Signum.MSBuildTask: {0}", e.Message);
                log.WriteLine(e.StackTrace);
                log.WriteLine(args[1]);
                return -1;
            }
        }

        static bool AlreadyProcessed(AssemblyDefinition assembly)
        {
            var nameof = typeof(GeneratedCodeAttribute).FullName;
            var attr = assembly.CustomAttributes
                .Any(a => a.AttributeType.FullName == nameof && ((string)a.ConstructorArguments[0].Value) == "SignumTask");

            return attr;
        }

        static void MarkAsProcessed(AssemblyDefinition assembly, IAssemblyResolver resolver)
        {
            TypeDefinition generatedCodeAttribute = resolver.Resolve(AssemblyNameReference.Parse(typeof(GeneratedCodeAttribute).Assembly.GetName().Name)).MainModule.GetType(typeof(GeneratedCodeAttribute).FullName);
            MethodDefinition constructor = generatedCodeAttribute.Methods.Single(a => a.IsConstructor && a.Parameters.Count == 2);

            TypeReference stringType = assembly.MainModule.TypeSystem.String;
            assembly.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.ImportReference(constructor))
            {
                ConstructorArguments =
                {
                    new CustomAttributeArgument(stringType, "SignumTask"),
                    new CustomAttributeArgument(stringType, typeof(Program).Assembly.GetName().Version.ToString()),
                }
            });
        }
    }
}
