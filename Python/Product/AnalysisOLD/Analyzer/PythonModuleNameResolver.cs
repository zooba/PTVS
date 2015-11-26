using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    //class PythonModuleNameResolver {
    //    private readonly PathSet<string> _searchPathSet;
    //    private readonly PathSet<string> _fileSet;
    //    private readonly List<string> _searchPaths;

    //    private static readonly Regex PythonPackageRegex = new Regex(@"^(?!\d)(?<name>(\w|_)+)$",
    //        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    //    private static readonly char[] DirSeparators =
    //        new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    //    public PythonModuleNameResolver() {
    //        _searchPathSet = new PathSet<string>();
    //        _fileSet = new PathSet<string>();
    //        _searchPaths = new List<string>();
    //    }

    //    public void AddFile(string fullPath) {
    //        if (!_searchPathSet.Contains(fullPath)) {
    //            throw new ArgumentException("File not in any search path");
    //        }
    //        var path = fullPath;
    //        if (path.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) {
    //            path = path.Substring(0, path.Length - 3);
    //        }
    //        if (path.EndsWith("__init__", StringComparison.Ordinal)) {
    //            path = path.Substring(0, path.Length - 8);
    //        }

    //        if (!_fileSet.Add(path, fullPath)) {
    //            throw new ArgumentException("File already exists");
    //        }
    //    }

    //    public void ClearFiles() {
    //        _fileSet.Clear();
    //    }

    //    public void AddSearchPath(string searchPath, string packageName) {
    //        if (!_searchPathSet.Add(searchPath, packageName)) {
    //            throw new ArgumentException("Search path already added");
    //        }
    //        _searchPaths.Add(searchPath);
    //    }

    //    public void ClearSearchPaths() {
    //        _searchPaths.Clear();
    //        _searchPathSet.Clear();
    //    }

    //    public string FindModule(string name) {
    //        var parts = name.Split('.');
    //        foreach (var searchPath in _searchPaths) {
    //            string fullPath;
    //            if (_fileSet.TryFindValueByParts(searchPath, parts, out fullPath)) {
    //                return fullPath;
    //            }
    //        }

    //        return null;
    //    }

    //    private static string MakeModuleName(string path, string packageName) {
    //        var modName = new StringBuilder(packageName);
    //        if (modName.Length > 0) {
    //            modName.Append('.');
    //        }

    //        for (int i = 0, j = 0; i < path.Length; i = j + 1) {
    //            j = path.IndexOfAny(DirSeparators, i);

    //            var m = PythonPackageRegex.Match(path, i);
    //            if (m.Length == (j < 0 ? path.Length : j) - i) {
    //                modName.Append(m.Value);
    //                if (j > 0) {
    //                    modName.Append('.');
    //                }
    //            } else {
    //                return null;
    //            }
    //        }

    //        return modName.ToString();
    //    }

    //    public string GetLongestModuleName(string filePath) {
    //        var residual = _searchPathSet.GetResiduals(filePath, 1).FirstOrDefault();
    //        if (string.IsNullOrEmpty(residual.Key)) {
    //            return null;
    //        }

    //        string packageName;
    //        if (!_searchPathSet.TryGetValue(residual.Key, out packageName)) {
    //            return null;
    //        }

    //        return MakeModuleName(residual.Value, packageName);
    //    }

    //    public IEnumerable<string> GetModuleNames(string filePath) {
    //        var residuals = _searchPathSet.GetResiduals(filePath);

    //        foreach (var residual in residuals) {
    //            string packageName;
    //            if (!_searchPathSet.TryGetValue(residual.Key, out packageName)) {
    //                continue;
    //            }

    //            yield return MakeModuleName(residual.Value, packageName);
    //        }
    //    }
    //}
}
