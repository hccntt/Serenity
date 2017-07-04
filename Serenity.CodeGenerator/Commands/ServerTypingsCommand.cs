﻿using Serenity.CodeGeneration;
using Serenity.Data;
using Serenity.Localization;
using Serenity.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Serenity.CodeGenerator
{
    public class ServerTypingsCommand
    {
        private static Encoding utf8 = new System.Text.UTF8Encoding(true);

        public void Run(string csproj, List<ExternalType> tsTypes)
        {
            var projectDir = Path.GetDirectoryName(csproj);
            var config = GeneratorConfig.LoadFromFile(Path.Combine(projectDir, "sergen.json"));

            if (config.ServerTypings == null)
            {
                System.Console.Error.WriteLine("ServerTypings is not configured in sergen.json file!");
                Environment.Exit(1);
            }

            if (config.ServerTypings.Assemblies.IsEmptyOrNull())
            {
                System.Console.Error.WriteLine("ServerTypings has no assemblies configured in sergen.json file!");
                Environment.Exit(1);
            }

            if (config.RootNamespace.IsEmptyOrNull())
            {
                System.Console.Error.WriteLine("Please set RootNamespace option in sergen.json file!");
                Environment.Exit(1);
            }

            var outDir = Path.Combine(projectDir, (config.ServerTypings.OutDir.TrimToNull() ?? "Imports/ServerTypings")
                .Replace('/', Path.DirectorySeparatorChar));

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Transforming ServerTypings at: ");
            Console.ResetColor();
            Console.WriteLine(outDir);

            var rootPath = Path.GetFullPath(config.ServerTypings.Assemblies[0].Replace('/', Path.DirectorySeparatorChar));

            List<Assembly> assemblies = new List<Assembly>();
            foreach (var assembly in config.ServerTypings.Assemblies)
            {
                var fullName = Path.GetFullPath(assembly.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullName))
                {
                    System.Console.Error.WriteLine(String.Format("Assembly file '{0}' specified in sergen.json is not found! " +
                        "This might happen when project is not successfully built or file name doesn't match the output DLL." +
                        "Please check path in sergen.json and try again.", fullName));
                    Environment.Exit(1);
                }

#if COREFX
                using (var dynamicContext = new AssemblyResolver(fullName))
                {
                    var asm = dynamicContext.Assembly;

                    try
                    {
                        asm.GetTypes();
                        assemblies.Add(asm);
                    }
                    catch (ReflectionTypeLoadException ex1)
                    {
                        System.Console.Error.WriteLine(String.Format("Couldn't list types in Assembly file '{0}' specified in sergen.json!", fullName) +
                            Environment.NewLine + Environment.NewLine +
                            string.Join(Environment.NewLine, ex1.LoaderExceptions.Select(x => x.Message).Distinct()));
                        Environment.Exit(1);
                    }
                    catch (Exception ex)
                    {
                        System.Console.Error.WriteLine(String.Format("Couldn't list types in Assembly file '{0}' specified in sergen.json! ", fullName)
                            + Environment.NewLine + Environment.NewLine + ex.ToString());
                        Environment.Exit(1);
                    }
                }
#else
                assemblies.Add(Assembly.LoadFrom(fullName));
#endif
            }

            Extensibility.ExtensibilityHelper.SelfAssemblies = new Assembly[]
            {
                typeof(LocalTextRegistry).Assembly,
                typeof(SqlConnections).Assembly,
                typeof(Row).Assembly,
                typeof(SaveRequestHandler<>).Assembly,
                typeof(WebSecurityHelper).Assembly
            }.Concat(assemblies).Distinct().ToArray();

            var generator = new ServerTypingsGenerator(assemblies.ToArray());
            generator.RootNamespaces.Add(config.RootNamespace);

            foreach (var type in tsTypes)
                generator.AddTSType(type);

            var codeByFilename = generator.Run();
            new MultipleOutputHelper().WriteFiles(outDir, codeByFilename, "*.ts");
        }
    }
}