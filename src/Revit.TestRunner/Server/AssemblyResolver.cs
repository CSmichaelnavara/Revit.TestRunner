using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Revit.TestRunner.Server
{
    /// <summary>
    /// Resolves assemblies from given directories
    /// </summary>
    public class AssemblyResolver
    {
        private AppDomain appDomain;

        private List<string> mDirectories = new List<string>();
        /// <summary>
        /// Creates new instance
        /// </summary>
        public AssemblyResolver()
        {
            appDomain = AppDomain.CurrentDomain;
        }

        /// <summary>
        /// Adds directory of given assembly to the list of
        /// directories where the Assemblies are searched.
        /// </summary>
        /// <param name="assembly">Assembly full file name</param>
        public void AddAssembly(string assembly)
        {
            //Add last directory to first position of list
            var assemblyDir = Path.GetDirectoryName(assembly);
            mDirectories.Insert(0, assemblyDir);
        }

        /// <summary>
        /// Starts assembly resolving
        /// </summary>
        public void Start()
        {
            appDomain.AssemblyResolve += AppDomain_AssemblyResolve;
        }

        /// <summary>
        /// Stops assembly resolving
        /// </summary>
        public void Stop()
        {
            appDomain.AssemblyResolve -= AppDomain_AssemblyResolve;
        }

        private Assembly AppDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var argsName = args.Name;
            foreach (var dir in mDirectories)
            foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            {
                var assembly = Assembly.LoadFile(file);
                if (assembly.FullName == argsName) return assembly;
            }

            return null;
        }
    }
}