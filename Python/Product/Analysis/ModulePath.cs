// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis {
    public struct ModulePath {
        public static readonly ModulePath Empty = new ModulePath(null, null, null);

        /// <summary>
        /// Returns true if the provided version of Python can only import
        /// packages containing an <c>__init__.py</c> file.
        /// </summary>
        public static bool PythonVersionRequiresInitPyFiles(Version languageVersion) {
            return languageVersion < new Version(3, 3);
        }

        /// <summary>
        /// The name by which the module can be imported in Python code.
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// The file containing the source for the module.
        /// </summary>
        public string SourceFile { get; set; }

        /// <summary>
        /// The path to the library containing the module.
        /// </summary>
        public string LibraryPath { get; set; }

        /// <summary>
        /// The last portion of <see cref="FullName"/>.
        /// </summary>
        public string Name {
            get {
                return FullName.Substring(FullName.LastIndexOf('.') + 1);
            }
        }

        /// <summary>
        /// True if the module is named '__main__' or '__init__'.
        /// </summary>
        public bool IsSpecialName {
            get {
                var name = Name;
                return name.Equals("__main__", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("__init__", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// The same as FullName unless the last part of the name is '__init__',
        /// in which case this is FullName without the last part.
        /// </summary>
        public string ModuleName {
            get {
                if (Name.Equals("__init__", StringComparison.OrdinalIgnoreCase)) {
                    int lastDot = FullName.LastIndexOf('.');
                    if (lastDot < 0) {
                        return string.Empty;
                    } else {
                        return FullName.Substring(0, lastDot);
                    }
                } else {
                    return FullName;
                }
            }
        }

        /// <summary>
        /// True if the module is a binary file.
        /// </summary>
        /// <remarks>Changed in 2.2 to include .pyc and .pyo files.</remarks>
        public bool IsCompiled {
            get {
                return PythonCompiledRegex.IsMatch(Path.GetFileName(SourceFile));
            }
        }

        /// <summary>
        /// True if the module is a native extension module.
        /// </summary>
        /// <remarks>New in 2.2</remarks>
        public bool IsNativeExtension {
            get {
                return PythonBinaryRegex.IsMatch(Path.GetFileName(SourceFile));
            }
        }

        /// <summary>
        /// Creates a new ModulePath item.
        /// </summary>
        /// <param name="fullname">The full name of the module.</param>
        /// <param name="sourceFile">The full path to the source file
        /// implementing the module.</param>
        /// <param name="libraryPath">
        /// The path to the library containing the module. This is typically a
        /// higher-level directory of <paramref name="sourceFile"/>.
        /// </param>
        public ModulePath(string fullname, string sourceFile, string libraryPath)
            : this() {
            FullName = fullname;
            SourceFile = sourceFile;
            LibraryPath = libraryPath;
        }

        private static readonly Regex PythonPackageRegex = new Regex(@"^(?!\d)(?<name>(\w|_)+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex PythonFileRegex = new Regex(@"^(?!\d)(?<name>(\w|_)+)\.pyw?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex PythonBinaryRegex = new Regex(@"^(?!\d)(?<name>(\w|_)+)\.((\w|_|-)+?\.)?pyd$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex PythonCompiledRegex = new Regex(@"^(?!\d)(?<name>(\w|_)+)\.(((\w|_|-)+?\.)?pyd|py[co])$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static IEnumerable<ModulePath> GetModuleNamesFromPathHelper(
            string libPath,
            string path,
            string baseModule,
            bool skipFiles,
            bool recurse,
            bool requireInitPy
        ) {
            Debug.Assert(baseModule == "" || baseModule.EndsWith("."));

            if (!Directory.Exists(path)) {
                yield break;
            }

            if (!skipFiles) {
                foreach (var file in PathUtils.EnumerateFiles(path, recurse: false)) {
                    var filename = PathUtils.GetFileOrDirectoryName(file);
                    var match = PythonFileRegex.Match(filename);
                    if (!match.Success) {
                        match = PythonBinaryRegex.Match(filename);
                    }
                    if (match.Success) {
                        var name = match.Groups["name"].Value;
                        if (name.EndsWith("_d") && file.EndsWith(".pyd", StringComparison.OrdinalIgnoreCase)) {
                            name = name.Remove(name.Length - 2);
                        }
                        yield return new ModulePath(baseModule + name, file, libPath ?? path);
                    }
                }
            }

            if (recurse) {
                foreach (var dir in PathUtils.EnumerateDirectories(path, recurse: false)) {
                    var dirname = PathUtils.GetFileOrDirectoryName(dir);
                    var match = PythonPackageRegex.Match(dirname);
                    if (match.Success && (!requireInitPy || File.Exists(Path.Combine(dir, "__init__.py")))) {
                        foreach (var entry in GetModuleNamesFromPathHelper(
                            skipFiles ? dir : libPath,
                            dir,
                            baseModule + match.Groups["name"].Value + ".",
                            false,
                            true,
                            requireInitPy
                        )) {
                            yield return entry;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns a sequence of ModulePath items for all modules importable
        /// from the provided path, optionally excluding top level files.
        /// </summary>
        public static IEnumerable<ModulePath> GetModulesInPath(
            string path,
            bool includeTopLevelFiles = true,
            bool recurse = true,
            string basePackage = null,
            bool requireInitPy = true
        ) {
            return GetModuleNamesFromPathHelper(
                path,
                path,
                basePackage ?? string.Empty,
                !includeTopLevelFiles,
                recurse,
                requireInitPy
            ).Where(mp => !string.IsNullOrEmpty(mp.ModuleName));
        }

        /// <summary>
        /// Returns a sequence of ModulePath items for all modules importable
        /// from the provided path, optionally excluding top level files.
        /// </summary>
        public static IEnumerable<ModulePath> GetModulesInPath(
            IEnumerable<string> paths,
            bool includeTopLevelFiles = true,
            bool recurse = true,
            string baseModule = null,
            bool requireInitPy = true
        ) {
            return paths.SelectMany(p => GetModuleNamesFromPathHelper(
                p,
                p,
                baseModule ?? string.Empty,
                !includeTopLevelFiles,
                recurse,
                requireInitPy
            )).Where(mp => !string.IsNullOrEmpty(mp.ModuleName));
        }

        /// <summary>
        /// Expands a sequence of directory paths to include any paths that are
        /// referenced in .pth files.
        /// 
        /// The original directories are not included in the result.
        /// </summary>
        public static IEnumerable<string> ExpandPathFiles(IEnumerable<string> paths) {
            foreach (var path in paths) {
                if (Directory.Exists(path)) {
                    foreach (var file in PathUtils.EnumerateFiles(path, "*.pth", recurse: false)) {
                        using (var reader = new StreamReader(file)) {
                            string line;
                            while ((line = reader.ReadLine()) != null) {
                                line = line.Trim();
                                if (line.StartsWith("import ", StringComparison.Ordinal) ||
                                    !PathUtils.IsValidPath(line)) {
                                    continue;
                                }
                                line = line.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                                if (!Path.IsPathRooted(line)) {
                                    line = PathUtils.GetAbsoluteDirectoryPath(path, line);
                                }
                                if (Directory.Exists(line)) {
                                    yield return line;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Expands a sequence of directory paths to include any paths that are
        /// referenced in .pth files.
        /// 
        /// The original directories are not included in the result.
        /// </summary>
        public static IEnumerable<string> ExpandPathFiles(params string[] paths) {
            return ExpandPathFiles(paths.AsEnumerable());
        }

        /// <summary>
        /// Returns a sequence of ModulePaths for all modules importable from
        /// the specified library.
        /// </summary>
        /// <remarks>
        /// Where possible, callers should use the methods from
        /// <see cref="PythonTypeDatabase"/> instead, as those are more accurate
        /// in the presence of non-standard Python installations. This function
        /// makes many assumptions about the install layout and may miss some
        /// modules.
        /// </remarks>
        public static IEnumerable<ModulePath> GetModulesInLib(
            string prefixPath,
            string libraryPath = null,
            string sitePath = null,
            bool requireInitPyFiles = true
        ) {
            if (File.Exists(prefixPath)) {
                prefixPath = Path.GetDirectoryName(prefixPath);
            }
            if (!Directory.Exists(libraryPath)) {
                libraryPath = Path.Combine(prefixPath, "Lib");
            }
            if (string.IsNullOrEmpty(sitePath)) {
                sitePath = Path.Combine(libraryPath, "site-packages");
            }
            var pthDirs = ExpandPathFiles(sitePath);
            var excludedPthDirs = new HashSet<string>() {
                sitePath,
                libraryPath
            };

            // Get modules in stdlib
            var modulesInStdLib = GetModulesInPath(libraryPath, true, true, requireInitPy: requireInitPyFiles);

            // Get files in site-packages
            var modulesInSitePackages = GetModulesInPath(sitePath, true, false, requireInitPy: requireInitPyFiles);

            // Get directories in site-packages
            // This is separate from getting files to ensure that each package
            // gets its own library path.
            var packagesInSitePackages = GetModulesInPath(sitePath, false, true, requireInitPy: requireInitPyFiles);

            // Get modules in DLLs directory
            IEnumerable<ModulePath> modulesInDllsPath;

            // Get modules in interpreter directory
            IEnumerable<ModulePath> modulesInExePath;

            if (Directory.Exists(prefixPath)) {
                modulesInDllsPath = GetModulesInPath(Path.Combine(prefixPath, "DLLs"), true, false);
                modulesInExePath = GetModulesInPath(prefixPath, true, false);
                excludedPthDirs.Add(prefixPath);
                excludedPthDirs.Add(Path.Combine(prefixPath, "DLLs"));
            } else {
                modulesInDllsPath = Enumerable.Empty<ModulePath>();
                modulesInExePath = Enumerable.Empty<ModulePath>();
            }

            // Get directories referenced by pth files
            var modulesInPath = GetModulesInPath(
                pthDirs.Where(p1 => excludedPthDirs.All(p2 => !PathUtils.IsSameDirectory(p1, p2))),
                true,
                true,
                requireInitPy: requireInitPyFiles
            );

            return modulesInPath
                .Concat(modulesInDllsPath)
                .Concat(modulesInStdLib)
                .Concat(modulesInExePath)
                .Concat(modulesInSitePackages)
                .Concat(packagesInSitePackages);
        }

        /// <summary>
        /// Returns a sequence of ModulePaths for all modules importable by the
        /// provided factory.
        /// </summary>
        public static IEnumerable<ModulePath> GetModulesInLib(InterpreterConfiguration config) {
            return GetModulesInLib(
                config.PrefixPath,
                null,   // default library path
                null,   // default site-packages path
                PythonVersionRequiresInitPyFiles(config.Version)
            );
        }

        /// <summary>
        /// Returns true if the provided path references an importable Python
        /// module. This function does not access the filesystem.
        /// Retuns false if an invalid string is provided. This function does
        /// not raise exceptions.
        /// </summary>
        public static bool IsPythonFile(string path) {
            return IsPythonFile(path, true, true, true);
        }

        /// <summary>
        /// Returns true if the provided path references an editable Python
        /// source module. This function does not access the filesystem.
        /// Retuns false if an invalid string is provided. This function does
        /// not raise exceptions.
        /// </summary>
        /// <remarks>
        /// This function may return true even if the file is not an importable
        /// module. Use <see cref="IsPythonFile"/> and specify "strict" to
        /// ensure the module can be imported.
        /// </remarks>
        public static bool IsPythonSourceFile(string path) {
            return IsPythonFile(path, false, false, false);
        }
        
        /// <summary>
        /// Returns true if the provided path references an importable Python
        /// module. This function does not access the filesystem.
        /// Retuns false if an invalid string is provided. This function does
        /// not raise exceptions.
        /// </summary>
        /// <param name="path">Path to check.</param>
        /// <param name="strict">
        /// True if the filename must be importable; false to allow unimportable
        /// names.
        /// </param>
        /// <param name="allowCompiled">
        /// True if pyd files should be allowed.
        /// </param>
        /// <param name="allowCache">
        /// True if pyc and pyo files should be allowed.
        /// </param>
        public static bool IsPythonFile(string path, bool strict, bool allowCompiled, bool allowCache) {
            if (string.IsNullOrEmpty(path)) {
                return false;
            }

            string name;
            try {
                name = PathUtils.GetFileOrDirectoryName(path);
            } catch (ArgumentException) {
                return false;
            }

            if (strict) {
                try {
                    var nameMatch = PythonFileRegex.Match(name);
                    if (allowCompiled && (nameMatch == null || !nameMatch.Success)) {
                        nameMatch = PythonCompiledRegex.Match(name);
                    }
                    return nameMatch != null && nameMatch.Success;
                } catch (RegexMatchTimeoutException) {
                    return false;
                }
            } else {
                var ext = name.Substring(name.LastIndexOf('.') + 1);
                return "py".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
                    "pyw".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
                    (allowCompiled && "pyd".Equals(ext, StringComparison.OrdinalIgnoreCase)) ||
                    (allowCache && (
                        "pyc".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
                        "pyo".Equals(ext, StringComparison.OrdinalIgnoreCase)
                    ));
            }
        }

        /// <summary>
        /// Returns true if the provided path is to an '__init__.py' file.
        /// Returns false if an invalid string is provided. This function does
        /// not raise exceptions.
        /// </summary>
        public static bool IsInitPyFile(string path) {
            if (string.IsNullOrEmpty(path)) {
                return false;
            }

            try {
                var name = Path.GetFileName(path);
                return name.Equals("__init__.py", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("__init__.pyw", StringComparison.OrdinalIgnoreCase);
            } catch (ArgumentException) {
                return false;
            }
        }

        /// <summary>
        /// Returns a new ModulePath value determined from the provided full
        /// path to a Python file. This function will access the filesystem to
        /// determine the package name.
        /// </summary>
        /// <param name="path">
        /// The path referring to a Python file.
        /// </param>
        /// <exception cref="ArgumentException">
        /// path is not a valid Python module.
        /// </exception>
        public static ModulePath FromFullPath(string path) {
            return FromFullPath(path, null, null);
        }

        /// <summary>
        /// Returns a new ModulePath value determined from the provided full
        /// path to a Python file. This function will access the filesystem to
        /// determine the package name.
        /// </summary>
        /// <param name="path">
        /// The path referring to a Python file.
        /// </param>
        /// <param name="topLevelPath">
        /// The directory to stop searching for packages at. The module name
        /// will never include the last segment of this path.
        /// </param>
        /// <exception cref="ArgumentException">
        /// path is not a valid Python module.
        /// </exception>
        /// <remarks>This overload </remarks>
        public static ModulePath FromFullPath(string path, string topLevelPath) {
            return FromFullPath(path, topLevelPath, null);
        }

        /// <summary>
        /// Returns a new ModulePath value determined from the provided full
        /// path to a Python file. This function may access the filesystem to
        /// determine the package name unless <paramref name="isPackage"/> is
        /// provided.
        /// </summary>
        /// <param name="path">
        /// The path referring to a Python file.
        /// </param>
        /// <param name="topLevelPath">
        /// The directory to stop searching for packages at. The module name
        /// will never include the last segment of this path.
        /// </param>
        /// <param name="isPackage">
        /// A predicate that determines whether the specified substring of
        /// <paramref name="path"/> represents a package. If omitted, the
        /// default behavior is to check for a file named "__init__.py" in the
        /// directory passed to the predicate.
        /// </param>
        /// <exception cref="ArgumentException">
        /// path is not a valid Python module.
        /// </exception>
        public static ModulePath FromFullPath(
            string path,
            string topLevelPath = null,
            Func<string, bool> isPackage = null
        ) {
            var name = PathUtils.GetFileOrDirectoryName(path);
            var nameMatch = PythonFileRegex.Match(name);
            if (nameMatch == null || !nameMatch.Success) {
                nameMatch = PythonBinaryRegex.Match(name);
            }
            if (nameMatch == null || !nameMatch.Success) {
                throw new ArgumentException("Not a valid Python module: " + path);
            }

            var fullName = nameMatch.Groups["name"].Value;
            var remainder = PathUtils.GetParent(path);
            if (isPackage == null) {
                // We know that f will be the result of GetParent() and always
                // ends with a directory separator, so just concatenate to avoid
                // potential path length problems.
                isPackage = f => File.Exists(f + "__init__.py");
            }

            while (
                PathUtils.IsValidPath(remainder) &&
                isPackage(remainder) &&
                (string.IsNullOrEmpty(topLevelPath) ||
                 (PathUtils.IsSubpathOf(topLevelPath, remainder) &&
                  !PathUtils.IsSameDirectory(topLevelPath, remainder)))
            ) {
                fullName = PathUtils.GetFileOrDirectoryName(remainder) + "." + fullName;
                remainder = PathUtils.GetParent(remainder);
            }

            return new ModulePath(fullName, path, remainder);
        }

        /// <summary>
        /// Returns a new ModulePath value determined from the provided search
        /// path and module name, if the module exists. This function may access
        /// the filesystem to determine the package name unless
        /// <paramref name="isPackage"/> and <param name="getModule"/> are
        /// provided.
        /// </summary>
        /// <param name="basePath">
        /// The path referring to a directory to start searching in.
        /// </param>
        /// <param name="moduleName">
        /// The full name of the module. If the name resolves to a package,
        /// "__init__" is automatically appended to the resulting name.
        /// </param>
        /// <param name="isPackage">
        /// A predicate that determines whether the specified substring of
        /// <paramref name="path"/> represents a package. If omitted, the
        /// default behavior is to check for a file named "__init__.py" in the
        /// directory passed to the predicate.
        /// </param>
        /// <param name="getModule">
        /// A function that returns valid module paths given a directory and a
        /// module name. The module name does not include any extension.
        /// For example, given "C:\Spam" and "eggs", this function may return
        /// one of "C:\Spam\eggs.py", "C:\Spam\eggs\__init__.py",
        /// "C:\Spam\eggs_d.cp35-win32.pyd" or some other full path. Returns
        /// null if there is no module importable by that name.
        /// </param>
        /// <exception cref="ArgumentException">
        /// moduleName is not a valid Python module.
        /// </exception>
        public static ModulePath FromBasePathAndName(
            string basePath,
            string moduleName,
            Func<string, bool> isPackage = null,
            Func<string, string, string> getModule = null
        ) {
            var bits = moduleName.Split('.');
            var lastBit = bits.Last();

            if (isPackage == null) {
                isPackage = f => Directory.Exists(f) && File.Exists(PathUtils.GetAbsoluteFilePath(f, "__init__.py"));
            }
            if (getModule == null) {
                getModule = (dir, mod) => {
                    var pack = PathUtils.GetAbsoluteFilePath(PathUtils.GetAbsoluteFilePath(dir, mod), "__init__.py");
                    if (File.Exists(pack)) {
                        return pack;
                    }
                    var mods = PathUtils.EnumerateFiles(dir, mod + "*", recurse: false).ToArray();
                    return mods.FirstOrDefault(p => PythonBinaryRegex.IsMatch(PathUtils.GetFileOrDirectoryName(p))) ??
                        mods.FirstOrDefault(p => PythonFileRegex.IsMatch(PathUtils.GetFileOrDirectoryName(p)));
                };
            }

            var path = basePath;

            foreach (var bit in bits.Take(bits.Length - 1)) {
                if (!PythonPackageRegex.IsMatch(bit)) {
                    throw new ArgumentException("Not a valid Python package: " + bit);
                }
                if (string.IsNullOrEmpty(path)) {
                    path = bit;
                } else {
                    path = PathUtils.GetAbsoluteFilePath(path, bit);
                }
                if (!isPackage(path)) {
                    throw new ArgumentException("Python package not found: " + path);
                }
            }

            if (!PythonPackageRegex.IsMatch(lastBit)) {
                throw new ArgumentException("Not a valid Python module: " + moduleName);
            }
            path = getModule(path, lastBit);
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentException("Python module not found: " + moduleName);
            }
            return new ModulePath(moduleName, path, basePath);
        }

        internal static IEnumerable<string> GetParents(string name, bool includeFullName = true) {
            if (string.IsNullOrEmpty(name)) {
                yield break;
            }

            var sb = new StringBuilder();
            var parts = name.Split('.');
            if (!includeFullName && parts.Length > 0) {
                parts[parts.Length - 1] = null;
            }

            foreach (var bit in parts.TakeWhile(s => !string.IsNullOrEmpty(s))) {
                sb.Append(bit);
                yield return sb.ToString();
                sb.Append('.');
            }
        }
    }
}
