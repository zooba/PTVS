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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter.Ast;
using Microsoft.PythonTools.Interpreter.Default;
using Microsoft.PythonTools.Parsing;

namespace Microsoft.PythonTools.Interpreter {
    /// <summary>
    /// Provides access to an on-disk store of cached intellisense information.
    /// </summary>
    public sealed class PythonTypeDatabase : ITypeDatabaseReader {
        private readonly PythonInterpreterFactoryWithDatabase _factory;
        private readonly SharedDatabaseState _sharedState;

        /// <summary>
        /// Gets the version of the analysis format that this class reads.
        /// </summary>
        public static readonly int CurrentVersion = 25;

        private static string _completionDatabasePath;
        private static string _referencesDatabasePath;
        private static string _baselineDatabasePath;

        public PythonTypeDatabase(
            PythonInterpreterFactoryWithDatabase factory,
            IEnumerable<string> databaseDirectories = null,
            PythonTypeDatabase innerDatabase = null
        ) {
            if (innerDatabase != null && factory.Configuration.Version != innerDatabase.LanguageVersion) {
                throw new InvalidOperationException("Language versions do not match");
            }

            _factory = factory;
            if (innerDatabase != null) {
                _sharedState = new SharedDatabaseState(innerDatabase._sharedState);
            } else {
                _sharedState = new SharedDatabaseState(_factory.Configuration.Version);
            }

            if (databaseDirectories != null) {
                foreach (var d in databaseDirectories) {
                    LoadDatabase(d);
                }
            }

            _sharedState.ListenForCorruptDatabase(this);
        }

        private PythonTypeDatabase(
            PythonInterpreterFactoryWithDatabase factory,
            string databaseDirectory,
            bool isDefaultDatabase
        ) {
            _factory = factory;
            _sharedState = new SharedDatabaseState(
                factory.Configuration.Version,
                databaseDirectory,
                defaultDatabase: isDefaultDatabase
            );
        }

        public PythonTypeDatabase Clone() {
            return new PythonTypeDatabase(_factory, null, this);
        }

        public PythonTypeDatabase CloneWithNewFactory(PythonInterpreterFactoryWithDatabase newFactory) {
            return new PythonTypeDatabase(newFactory, null, this);
        }

        public PythonTypeDatabase CloneWithNewBuiltins(IBuiltinPythonModule newBuiltins) {
            var newDb = new PythonTypeDatabase(_factory, null, this);
            newDb._sharedState.BuiltinModule = newBuiltins;
            return newDb;
        }

        public IPythonInterpreterFactoryWithDatabase InterpreterFactory {
            get {
                return _factory;
            }
        }

        /// <summary>
        /// Gets the Python version associated with this database.
        /// </summary>
        public Version LanguageVersion {
            get {
                return _factory.Configuration.Version;
            }
        }

        /// <summary>
        /// Loads modules from the specified path. Except for a builtins module,
        /// these will override any currently loaded modules.
        /// </summary>
        public void LoadDatabase(string databasePath) {
            _sharedState.LoadDatabase(databasePath);
        }

        /// <summary>
        /// Asynchrously loads the specified extension module into the type
        /// database making the completions available.
        /// 
        /// If the module has not already been analyzed it will be analyzed and
        /// then loaded.
        /// 
        /// If the specified module was already loaded it replaces the existing
        /// module.
        /// 
        /// Returns a new Task which can be blocked upon until the analysis of
        /// the new extension module is available.
        /// 
        /// If the extension module cannot be analyzed an exception is reproted.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token which can be
        /// used to cancel the async loading of the module</param>
        /// <param name="extensionModuleFilename">The filename of the extension
        /// module to be loaded</param>
        /// <param name="interpreter">The Python interprefer which will be used
        /// to analyze the extension module.</param>
        /// <param name="moduleName">The module name of the extension module.</param>
        public void LoadExtensionModule(
            ModulePath moduleName,
            CancellationToken cancellationToken = default(CancellationToken)
        ) {
            var loader = new ExtensionModuleLoader(
                this,
                _factory,
                moduleName,
                cancellationToken
            );
            loader.LoadExtensionModule();
        }

        public void AddModule(string moduleName, IPythonModule module) {
            _sharedState.Modules[moduleName] = module;
        }

        public bool UnloadModule(string moduleName) {
            IPythonModule dummy;
            return _sharedState.Modules.TryRemove(moduleName, out dummy);
        }

        private static Task MakeExceptionTask(Exception e) {
            var res = new TaskCompletionSource<Task>();
            res.SetException(e);
            return res.Task;
        }

        internal class ExtensionModuleLoader {
            private readonly PythonTypeDatabase _typeDb;
            private readonly IPythonInterpreterFactory _factory;
            private readonly ModulePath _moduleName;
            private readonly CancellationToken _cancel;

            const string _extensionModuleInfoFile = "extensions.$list";

            public ExtensionModuleLoader(PythonTypeDatabase typeDb, IPythonInterpreterFactory factory, ModulePath moduleName, CancellationToken cancel) {
                _typeDb = typeDb;
                _factory = factory;
                _moduleName = moduleName;
                _cancel = cancel;
            }

            public void LoadExtensionModule() {
                List<string> existingModules = new List<string>();
                string dbFile = null;
                // open the file locking it - only one person can look at the "database" of per-project analysis.
                using (var fs = OpenProjectExtensionList()) {
                    dbFile = FindDbFile(_factory, _moduleName.SourceFile, existingModules, dbFile, fs);

                    if (dbFile == null) {
                        dbFile = GenerateDbFile(_factory, _moduleName, existingModules, dbFile, fs);
                    }
                }

                _typeDb._sharedState.Modules[_moduleName.FullName] = new CPythonModule(_typeDb, _moduleName.FullName, dbFile, false);
            }

            private FileStream OpenProjectExtensionList() {
                Directory.CreateDirectory(ReferencesDatabasePath);

                for (int i = 0; i < 50 && !_cancel.IsCancellationRequested; i++) {
                    try {
                        return new FileStream(Path.Combine(ReferencesDatabasePath, _extensionModuleInfoFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    } catch (IOException) {
                        if (_cancel.CanBeCanceled) {
                            _cancel.WaitHandle.WaitOne(100);
                        } else {
                            Thread.Sleep(100);
                        }
                    }
                }

                throw new CannotAnalyzeExtensionException("Cannot access per-project extension registry.");
            }

            private string GenerateDbFile(IPythonInterpreterFactory interpreter, ModulePath moduleName, List<string> existingModules, string dbFile, FileStream fs) {
                // we need to generate the DB file
                dbFile = Path.Combine(ReferencesDatabasePath, moduleName + ".$project.idb");
                int retryCount = 0;
                while (File.Exists(dbFile)) {
                    dbFile = Path.Combine(ReferencesDatabasePath, moduleName + "." + ++retryCount + ".$project.idb");
                }

                var args = new List<string> {
                    PythonToolsInstallPath.GetFile("ExtensionScraper.py"),
                    "scrape",
                };

                if (moduleName.IsNativeExtension) {
                    args.Add("-");
                    args.Add(moduleName.SourceFile);
                } else {
                    args.Add(moduleName.ModuleName);
                    args.Add(moduleName.LibraryPath);
                }
                args.Add(Path.ChangeExtension(dbFile, null));

                using (var output = interpreter.Run(args.ToArray())) {
                    if (_cancel.CanBeCanceled) {
                        if (WaitHandle.WaitAny(new[] { _cancel.WaitHandle, output.WaitHandle }) != 1) {
                            // we were cancelled
                            return null;
                        }
                    } else {
                        output.Wait();
                    }

                    if (output.ExitCode == 0) {
                        // [FileName]|interpGuid|interpVersion|DateTimeStamp|[db_file.idb]
                        // save the new entry in the DB file
                        existingModules.Add(
                            String.Format("{0}|{1}|{2}|{3}",
                                moduleName.SourceFile,
                                interpreter.Configuration.Id,
                                new FileInfo(moduleName.SourceFile).LastWriteTime.ToString("O"),
                                dbFile
                            )
                        );

                        fs.Seek(0, SeekOrigin.Begin);
                        fs.SetLength(0);
                        using (var sw = new StreamWriter(fs)) {
                            sw.Write(String.Join(Environment.NewLine, existingModules));
                            sw.Flush();
                        }
                    } else {
                        throw new CannotAnalyzeExtensionException(string.Join(Environment.NewLine, output.StandardErrorLines));
                    }
                }

                return dbFile;
            }

            const int extensionModuleFilenameIndex = 0;
            const int interpreteIdIndex = 1;
            const int extensionTimeStamp = 2;
            const int dbFileIndex = 3;

            internal static bool AlwaysGenerateDb = false;

            /// <summary>
            /// Finds the appropriate entry in our database file and returns the name of the .idb file to be loaded or null
            /// if we do not have a generated .idb file.
            /// </summary>
            private static string FindDbFile(IPythonInterpreterFactory interpreter, string extensionModuleFilename, List<string> existingModules, string dbFile, FileStream fs) {
                if (AlwaysGenerateDb) {
                    return null;
                }

                var reader = new StreamReader(fs);

                string line;
                while ((line = reader.ReadLine()) != null) {
                    // [FileName]|interpId|DateTimeStamp|[db_file.idb]
                    string[] columns = line.Split('|');

                    if (columns.Length != 5) {
                        // malformed data...
                        continue;
                    }

                    if (File.Exists(columns[dbFileIndex])) {
                        // db file still exists
                        DateTime lastModified;
                        if (!File.Exists(columns[extensionModuleFilenameIndex]) ||  // extension has been deleted
                            !DateTime.TryParseExact(columns[extensionTimeStamp], "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out lastModified) ||
                            lastModified != File.GetLastWriteTime(columns[extensionModuleFilenameIndex])) { // extension has been modified

                            // cleanup the stale DB files as we go...
                            try {
                                File.Delete(columns[dbFileIndex]);
                            } catch (IOException) {
                            } catch (UnauthorizedAccessException) {
                            }
                            continue;
                        }
                    } else {
                        continue;
                    }

                    // check if this is the file we're looking for...
                    if (columns[interpreteIdIndex] != interpreter.Configuration.Id ||
                        String.Compare(columns[extensionModuleFilenameIndex], extensionModuleFilename, StringComparison.OrdinalIgnoreCase) != 0) {   // not our interpreter

                        // nope, but remember the line for when we re-write out the DB.
                        existingModules.Add(line);
                        continue;
                    }

                    // this is our file, but continue reading the other lines for when we write out the DB...
                    dbFile = columns[dbFileIndex];
                }
                return dbFile;
            }
        }

        public static PythonTypeDatabase CreateDefaultTypeDatabase(PythonInterpreterFactoryWithDatabase factory) {
            return new PythonTypeDatabase(factory, BaselineDatabasePath, isDefaultDatabase: true);
        }

        internal static PythonTypeDatabase CreateDefaultTypeDatabase() {
            return CreateDefaultTypeDatabase(new Version(2, 7));
        }

        internal static PythonTypeDatabase CreateDefaultTypeDatabase(Version languageVersion) {
            return new PythonTypeDatabase(InterpreterFactoryCreator.CreateAnalysisInterpreterFactory(languageVersion),
                BaselineDatabasePath, isDefaultDatabase: true);
        }

        public IEnumerable<string> GetModuleNames() {
            return _sharedState.GetModuleNames();
        }

        public IPythonModule GetModule(string name) {
            return _sharedState.GetModule(name);
        }

        public string DatabaseDirectory {
            get {
                return _sharedState.DatabaseDirectory;
            }
        }

        public IBuiltinPythonModule BuiltinModule {
            get {
                return _sharedState.BuiltinModule;
            }
        }

        /// <summary>
        /// The exit code returned when database generation fails due to an
        /// invalid argument.
        /// </summary>
        public const int InvalidArgumentExitCode = -1;

        /// <summary>
        /// The exit code returned when database generation fails due to a
        /// non-specific error.
        /// </summary>
        public const int InvalidOperationExitCode = -2;

        /// <summary>
        /// The exit code returned when a database is already being generated
        /// for the interpreter factory.
        /// </summary>
        public const int AlreadyGeneratingExitCode = -3;

        /// <summary>
        /// The exit code returned when a database cannot be created for the
        /// interpreter factory.
        /// </summary>
        public const int NotSupportedExitCode = -4;

        public static async Task<int> GenerateAsync(PythonTypeDatabaseCreationRequest request) {
            var fact = request.Factory;
            var evt = request.OnExit;
            if (fact == null) {
                evt?.Invoke(NotSupportedExitCode);
                return NotSupportedExitCode;
            }
            var outPath = request.OutputPath;

            var analyzerPath = PythonToolsInstallPath.GetFile("Microsoft.PythonTools.Analyzer.exe");

            Directory.CreateDirectory(CompletionDatabasePath);

            var baseDb = BaselineDatabasePath;
            if (request.ExtraInputDatabases.Any()) {
                baseDb = baseDb + ";" + string.Join(";", request.ExtraInputDatabases);
            }

            var logPath = Path.Combine(outPath, "AnalysisLog.txt");
            var glogPath = Path.Combine(CompletionDatabasePath, "AnalysisLog.txt");

            using (var output = ProcessOutput.RunHiddenAndCapture(
                analyzerPath,
                "/id", fact.Configuration.Id,
                "/version", fact.Configuration.Version.ToString(),
                "/python", fact.Configuration.InterpreterPath,
                "/outdir", outPath,
                "/basedb", baseDb,
                (request.SkipUnchanged ? null : "/all"),  // null will be filtered out; empty strings are quoted
                "/log", logPath,
                "/glog", glogPath,
                "/wait", (request.WaitFor != null ? AnalyzerStatusUpdater.GetIdentifier(request.WaitFor) : "")
            )) {
                output.PriorityClass = ProcessPriorityClass.BelowNormal;
                int exitCode = await output;

                if (exitCode > -10 && exitCode < 0) {
                    try {
                        File.AppendAllLines(
                            glogPath,
                            new[] { string.Format("FAIL_STDLIB: ({0}) {1}", exitCode, output.Arguments) }
                                .Concat(output.StandardErrorLines)
                        );
                    } catch (IOException) {
                    } catch (ArgumentException) {
                    } catch (SecurityException) {
                    } catch (UnauthorizedAccessException) {
                    }
                }

                if (evt != null) {
                    evt(exitCode);
                }
                return exitCode;
            }
        }

        /// <summary>
        /// Invokes Analyzer.exe for the specified factory.
        /// </summary>
        [Obsolete("Use GenerateAsync instead")]
        public static void Generate(PythonTypeDatabaseCreationRequest request) {
            var onExit = request.OnExit;

            GenerateAsync(request).ContinueWith(t => {
                var exc = t.Exception;
                if (exc == null) {
                    return;
                }

                try {
                    var message = string.Format(
                        "ERROR_STDLIB: {0}\\{1}{2}",
                        request.Factory.Configuration.Id,
                        Environment.NewLine,
                        (exc.InnerException ?? exc).ToString()
                    );

                    Debug.WriteLine(message);

                    var glogPath = Path.Combine(CompletionDatabasePath, "AnalysisLog.txt");
                    File.AppendAllText(glogPath, message);
                } catch (IOException) {
                } catch (ArgumentException) {
                } catch (SecurityException) {
                } catch (UnauthorizedAccessException) {
                }

                if (onExit != null) {
                    onExit(PythonTypeDatabase.InvalidOperationExitCode);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static bool DatabaseExists(string path) {
            string versionFile = Path.Combine(path, "database.ver");
            if (File.Exists(versionFile)) {
                try {
                    string allLines = File.ReadAllText(versionFile);
                    int version;
                    return Int32.TryParse(allLines, out version) && version == PythonTypeDatabase.CurrentVersion;
                } catch (IOException) {
                }
            }
            return false;
        }

        public static string GlobalLogFilename {
            get {
                return Path.Combine(CompletionDatabasePath, "AnalysisLog.txt");
            }
        }

        internal static string BaselineDatabasePath {
            get {
                if (_baselineDatabasePath == null) {
                    _baselineDatabasePath = Path.GetDirectoryName(
                        PythonToolsInstallPath.GetFile("CompletionDB\\__builtin__.idb")
                    );
                }
                return _baselineDatabasePath;
            }
        }

        public static string CompletionDatabasePath {
            get {
                if (_completionDatabasePath == null) {
                    _completionDatabasePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Python Tools",
                        "CompletionDB",
#if DEBUG
                        "Debug",
#endif
                        AssemblyVersionInfo.Version
                    );
                }
                return _completionDatabasePath;
            }
        }

        private static string ReferencesDatabasePath {
            get {
                if (_referencesDatabasePath == null) {
                    _referencesDatabasePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Python Tools",
                        "ReferencesDB",
#if DEBUG
                        "Debug",
#endif
                        AssemblyVersionInfo.Version
                    );
                }
                return _referencesDatabasePath;
            }
        }

        void ITypeDatabaseReader.LookupType(object type, Action<IPythonType> assign) {
            _sharedState.LookupType(type, assign);
        }

        string ITypeDatabaseReader.GetBuiltinTypeName(BuiltinTypeId id) {
            return _sharedState.GetBuiltinTypeName(id);
        }

        void ITypeDatabaseReader.ReadMember(string memberName, Dictionary<string, object> memberValue, Action<string, IMember> assign, IMemberContainer container) {
            _sharedState.ReadMember(memberName, memberValue, assign, container);
        }

        void ITypeDatabaseReader.OnDatabaseCorrupt() {
            OnDatabaseCorrupt();
        }

        public void OnDatabaseCorrupt() {
            _factory.NotifyCorruptDatabase();
        }

        internal CPythonConstant GetConstant(IPythonType type) {
            return _sharedState.GetConstant(type);
        }

        internal static bool TryGetLocation(Dictionary<string, object> table, ref int line, ref int column) {
            object value;
            if (table.TryGetValue("location", out value)) {
                object[] locationInfo = value as object[];
                if (locationInfo != null && locationInfo.Length == 2 && locationInfo[0] is int && locationInfo[1] is int) {
                    line = (int)locationInfo[0];
                    column = (int)locationInfo[1];
                    return true;
                }
            }
            return false;
        }


        public bool BeginModuleLoad(IPythonModule module, int millisecondsTimeout) {
            return _sharedState.BeginModuleLoad(module, millisecondsTimeout);
        }

        public void EndModuleLoad(IPythonModule module) {
            _sharedState.EndModuleLoad(module);
        }

        /// <summary>
        /// Returns true if the specified database has a version specified that
        /// matches the current build of PythonTypeDatabase. If false, attempts
        /// to load the database may fail with an exception.
        /// </summary>
        public static bool IsDatabaseVersionCurrent(string databasePath) {
            if (// Also ensures databasePath won't crash Path.Combine()
                Directory.Exists(databasePath) &&
                // Ensures that the database is not currently regenerating
                !File.Exists(Path.Combine(databasePath, "database.pid"))) {
                string versionFile = Path.Combine(databasePath, "database.ver");
                if (File.Exists(versionFile)) {
                    try {
                        return int.Parse(File.ReadAllText(versionFile)) == CurrentVersion;
                    } catch (IOException) {
                    } catch (UnauthorizedAccessException) {
                    } catch (SecurityException) {
                    } catch (InvalidOperationException) {
                    } catch (ArgumentException) {
                    } catch (OverflowException) {
                    } catch (FormatException) {
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if the specified database is currently regenerating.
        /// </summary>
        public static bool IsDatabaseRegenerating(string databasePath) {
            return Directory.Exists(databasePath) &&
                File.Exists(Path.Combine(databasePath, "database.pid"));
        }

        /// <summary>
        /// Gets the default set of search paths based on the path to the root
        /// of the standard library.
        /// </summary>
        /// <param name="library">Root of the standard library.</param>
        /// <returns>A list of search paths for the interpreter.</returns>
        /// <remarks>New in 2.2</remarks>
        public static List<PythonLibraryPath> GetDefaultDatabaseSearchPaths(string library) {
            var result = new List<PythonLibraryPath>();
            if (!Directory.Exists(library)) {
                return result;
            }

            result.Add(new PythonLibraryPath(library, true, null));

            var sitePackages = Path.Combine(library, "site-packages");
            if (!Directory.Exists(sitePackages)) {
                return result;
            }

            result.Add(new PythonLibraryPath(sitePackages, false, null));
            result.AddRange(ModulePath.ExpandPathFiles(sitePackages)
                .Select(p => new PythonLibraryPath(p, false, null))
            );

            return result;
        }

        /// <summary>
        /// Gets the set of search paths for the specified factory as
        /// efficiently as possible. This may involve executing the
        /// interpreter, and may cache the paths for retrieval later.
        /// </summary>
        public static async Task<IList<PythonLibraryPath>> GetDatabaseSearchPathsAsync(IPythonInterpreterFactory factory) {
            var dbPath = (factory as PythonInterpreterFactoryWithDatabase)?.DatabasePath;
            for (int retries = 5; retries > 0; --retries) {
                if (!string.IsNullOrEmpty(dbPath)) {
                    var paths = GetCachedDatabaseSearchPaths(dbPath);
                    if (paths != null) {
                        return paths;
                    }
                }

                try {
                    var paths = await GetUncachedDatabaseSearchPathsAsync(factory.Configuration.InterpreterPath);
                    if (!string.IsNullOrEmpty(dbPath)) {
                        WriteDatabaseSearchPaths(dbPath, paths);
                    }
                    return paths;
                } catch (InvalidOperationException) {
                    // Failed to get paths
                    break;
                } catch (IOException) {
                    // Failed to write paths - sleep and then loop
                    Thread.Sleep(50);
                }
            }

            var ospy = PathUtils.FindFile(factory.Configuration.PrefixPath, "os.py", firstCheck: new[] { "Lib" });
            if (!string.IsNullOrEmpty(ospy)) {
                return GetDefaultDatabaseSearchPaths(PathUtils.GetParent(ospy));
            }

            return Array.Empty<PythonLibraryPath>();
        }

        /// <summary>
        /// Gets the set of search paths by running the interpreter.
        /// </summary>
        /// <param name="interpreter">Path to the interpreter.</param>
        /// <returns>A list of search paths for the interpreter.</returns>
        /// <remarks>Added in 2.2</remarks>
        public static async Task<List<PythonLibraryPath>> GetUncachedDatabaseSearchPathsAsync(string interpreter) {
            List<string> lines;

            // sys.path will include the working directory, so we make an empty
            // path that we can filter out later
            var tempWorkingDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempWorkingDir);
            var srcGetSearchPaths = PythonToolsInstallPath.GetFile("get_search_paths.py", typeof(PythonTypeDatabase).Assembly);
            var getSearchPaths = PathUtils.GetAbsoluteFilePath(tempWorkingDir, PathUtils.GetFileOrDirectoryName(srcGetSearchPaths));
            File.Copy(srcGetSearchPaths, getSearchPaths);

            try {
                using (var proc = ProcessOutput.Run(
                    interpreter,
                    new[] {
                        "-S",   // don't import site - we do that in code
                        "-E",   // ignore environment
                        getSearchPaths
                    },
                    tempWorkingDir,
                    null,
                    false,
                    null
                )) {
                    int exitCode = -1;
                    try {
                        exitCode = await proc;
                    } catch (OperationCanceledException) {
                    }
                    if (exitCode != 0) {
                        throw new InvalidOperationException(string.Format(
                            "Cannot obtain list of paths{0}{1}",
                            Environment.NewLine,
                            string.Join(Environment.NewLine, proc.StandardErrorLines))
                        );
                    }

                    lines = proc.StandardOutputLines.ToList();
                }
            } finally {
                try {
                    Directory.Delete(tempWorkingDir, true);
                } catch (Exception ex) {
                    if (ex.IsCriticalException()) {
                        throw;
                    }
                }
            }

            return lines.Select(s => {
                if (s.StartsWith(tempWorkingDir, StringComparison.OrdinalIgnoreCase)) {
                    return null;
                }
                try {
                    return PythonLibraryPath.Parse(s);
                } catch (FormatException) {
                    Debug.Fail("Invalid format for search path: " + s);
                    return null;
                }
            }).Where(p => p != null).ToList();
        }

        /// <summary>
        /// Gets the set of search paths that were last saved for a database.
        /// </summary>
        /// <param name="databasePath">Path containing the database.</param>
        /// <returns>The cached list of search paths.</returns>
        /// <remarks>Added in 2.2</remarks>
        public static List<PythonLibraryPath> GetCachedDatabaseSearchPaths(string databasePath) {
            var cacheFile = Path.Combine(databasePath, "database.path");
            if (!File.Exists(cacheFile)) {
                return null;
            }
            if (!IsDatabaseVersionCurrent(databasePath)) {
                // Cache file with no database is only valid for one hour
                try {
                    var time = File.GetLastWriteTimeUtc(cacheFile);
                    if (time.AddHours(1) < DateTime.UtcNow) {
                        File.Delete(cacheFile);
                        return null;
                    }
                } catch (IOException) {
                    return null;
                }
            }


            try {
                var result = new List<PythonLibraryPath>();
                using (var file = File.OpenText(cacheFile)) {
                    string line;
                    while ((line = file.ReadLine()) != null) {
                        try {
                            result.Add(PythonLibraryPath.Parse(line));
                        } catch (FormatException) {
                            Debug.Fail("Invalid format: " + line);
                        }
                    }
                }

                return result;
            } catch (IOException) {
                return null;
            }
        }

        /// <summary>
        /// Saves search paths for a database.
        /// </summary>
        /// <param name="databasePath">The path to the database.</param>
        /// <param name="paths">The list of search paths.</param>
        /// <remarks>Added in 2.2</remarks>
        public static void WriteDatabaseSearchPaths(string databasePath, IEnumerable<PythonLibraryPath> paths) {
            Directory.CreateDirectory(databasePath);
            using (var file = new StreamWriter(Path.Combine(databasePath, "database.path"))) {
                foreach (var path in paths) {
                    file.WriteLine(path.ToString());
                }
            }
        }

        /// <summary>
        /// Returns ModulePaths representing the modules that should be analyzed
        /// for the given search paths.
        /// </summary>
        /// <param name="languageVersion">
        /// The Python language version to assume. This affects whether
        /// namespace packages are supported or not.
        /// </param>
        /// <param name="searchPaths">A sequence of paths to search.</param>
        /// <returns>
        /// All the expected modules, grouped based on codependency. When
        /// analyzing modules, all those in the same list should be analyzed
        /// together.
        /// </returns>
        /// <remarks>Added in 2.2</remarks>
        public static IEnumerable<List<ModulePath>> GetDatabaseExpectedModules(
            Version languageVersion,
            IEnumerable<PythonLibraryPath> searchPaths
        ) {
            var requireInitPy = ModulePath.PythonVersionRequiresInitPyFiles(languageVersion);

            var stdlibGroup = new List<ModulePath>();
            var packages = new List<List<ModulePath>> { stdlibGroup };

            foreach (var path in searchPaths ?? Enumerable.Empty<PythonLibraryPath>()) {
                if (path.IsStandardLibrary) {
                    stdlibGroup.AddRange(ModulePath.GetModulesInPath(
                        path.Path,
                        includeTopLevelFiles: true,
                        recurse: true,
                        // Always require __init__.py for stdlib folders
                        // Otherwise we will probably include libraries multiple
                        // times, and while Python 3.3+ allows this, it's really
                        // not a good idea.
                        requireInitPy: true
                    ));
                } else {
                    packages.Add(ModulePath.GetModulesInPath(
                        path.Path,
                        includeTopLevelFiles: true,
                        recurse: false,
                        basePackage: path.ModulePrefix
                    ).ToList());
                    packages.AddRange(ModulePath.GetModulesInPath(
                        path.Path,
                        includeTopLevelFiles: false,
                        recurse: true,
                        basePackage: path.ModulePrefix,
                        requireInitPy: requireInitPy
                    ).GroupBy(g => g.LibraryPath).Select(g => g.ToList()));
                }
            }

            return packages;
        }

    }

}
