using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using JetBrains.Annotations;
using UniLinq;

// ReSharper disable MemberCanBePrivate.Global

namespace KSPBurst
{
    public static class Compression
    {
        public const string ArchiveInfoFile = "." + PathUtil.ModName;
        [NotNull] public static readonly string[] ArchiveExtensions = {".zip", ".rar", ".7z", ".tgz"};

        public static bool IsArchive([NotNull] string filename)
        {
            if (filename is null) throw new ArgumentNullException(nameof(filename));

            return ArchiveExtensions.Any(
                extension => filename.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        public static void ExtractArchive([NotNull] string archivePath, [NotNull] string destination,
            [NotNull] string infoFilename = ArchiveInfoFile)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(archivePath));
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(destination));
            if (string.IsNullOrWhiteSpace(infoFilename))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(infoFilename));
            if (Directory.Exists(destination))
                throw new IOException($"{destination} already exists");

            List<string> files = new();
            HashSet<string> directories = new();
            string infoFilePath = Path.Combine(destination, infoFilename);

            try
            {
                using ZipArchive zip = ZipFile.OpenRead(archivePath);

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string destinationPath = Path.Combine(destination, entry.FullName);
                    string destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                        Directory.CreateDirectory(destinationDir);
                    entry.ExtractToFile(destinationPath);

                    // store relative paths
                    if (!string.IsNullOrEmpty(entry.FullName)) files.Add(entry.FullName);
                    string entryDir = Path.GetDirectoryName(entry.FullName);
                    if (!string.IsNullOrEmpty(entryDir)) directories.Add(entryDir);
                }

                // use reversed directories to put child directories higher, seems to work at least for burst package
                files.AddRange(directories.Reverse());
                WriteFileList(infoFilePath, files);

                KSPBurst.Log($"{archivePath} extracted to {destination}");
            }
            catch
            {
                KSPBurst.LogError($"Error extracting archive {archivePath} to {destination}");

                // remove extracted files
                if (File.Exists(infoFilePath)) File.Delete(infoFilePath);
                RemoveFiles(destination, files);
                RemoveFiles(destination, directories.Reverse());

                throw;
            }
        }

        public static void CleanOldFiles([NotNull] string directory, [NotNull] string infoFilename = ArchiveInfoFile)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(directory));
            if (string.IsNullOrWhiteSpace(infoFilename))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(infoFilename));

            foreach (string infoFile in PathUtil.Glob(directory, $"**/{infoFilename}"))
            {
                string dirname = Path.GetDirectoryName(infoFile);
                if (dirname is null) continue;

                KSPBurst.Log($"Cleaning old files from '{dirname}'");
                string[] fileList = ReadFileList(infoFile);
                File.Delete(infoFile);
                AssemblyUtil.DeleteCache(dirname);
                RemoveFiles(dirname, fileList);
            }
        }

        // ReSharper disable once ReturnTypeCanBeEnumerable.Local
        [NotNull]
        private static string[] ReadFileList([NotNull] string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(filename));

            return !File.Exists(filename) ? Array.Empty<string>() : File.ReadAllLines(filename);
        }

        private static void WriteFileList([NotNull] string filename, [NotNull] IEnumerable<string> files)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(filename));
            if (files is null) throw new ArgumentNullException(nameof(files));

            File.WriteAllLines(filename, files);
        }

        private static void TryDeleteDirectory([CanBeNull] string directory)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    Directory.Delete(directory);
            }
            catch (IOException)
            {
                // IOException will be thrown if directory is not empty, just skip
            }
        }

        private static void RemoveFiles([NotNull] string directory, [NotNull] IEnumerable<string> files)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (files is null) throw new ArgumentNullException(nameof(files));

            foreach (string filename in files)
            {
                string name = Path.Combine(directory, filename);
                if (File.Exists(name)) File.Delete(name);
                else if (Directory.Exists(name)) TryDeleteDirectory(name);
            }

            TryDeleteDirectory(directory);
        }
    }
}