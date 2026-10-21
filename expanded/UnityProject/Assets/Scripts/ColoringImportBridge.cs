using System;
using System.IO;
using UnityEngine;

// Watches the existing photo-processing output and gives the newest transparent
// coloring to the matching animal. The bridge deliberately stays independent
// from the Python/OpenCV process: the two sides communicate through PNG files.
public sealed class ColoringImportBridge : MonoBehaviour
{
    public string relativeOutputDirectory = "ImagePipeline/output";
    public string filePattern = "*_transparent.png";
    public float scanIntervalSeconds = 1f;

    ExhibitionController exhibition;
    string loadedPath;
    DateTime loadedWriteTimeUtc;
    float nextScan;

    public string CurrentSource => string.IsNullOrEmpty(loadedPath) ? "未読込" : Path.GetFileName(loadedPath);

    void Start()
    {
        exhibition = GetComponent<ExhibitionController>();
        Scan();
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        Scan();
    }

    void Scan()
    {
        nextScan = Time.unscaledTime + Mathf.Max(.25f, scanIntervalSeconds);
        if (exhibition == null) return;

        string directory = ResolveOutputDirectory();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        string newest = null;
        DateTime newestWriteTime = DateTime.MinValue;
        try
        {
            foreach (string path in Directory.EnumerateFiles(directory, filePattern, SearchOption.TopDirectoryOnly))
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if (writeTime <= newestWriteTime) continue;
                newest = path;
                newestWriteTime = writeTime;
            }
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        if (string.IsNullOrEmpty(newest)) return;
        if (newest == loadedPath && newestWriteTime == loadedWriteTimeUtc) return;

        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = Path.GetFileNameWithoutExtension(newest),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.LoadImage(File.ReadAllBytes(newest), false))
            {
                Destroy(texture);
                return;
            }

            exhibition.SetImportedColoring(texture, Path.GetFileName(newest));
            loadedPath = newest;
            loadedWriteTimeUtc = newestWriteTime;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    string ResolveOutputDirectory()
    {
        const string argumentPrefix = "-coloringOutput=";
        foreach (string argument in Environment.GetCommandLineArgs())
            if (argument.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(argument.Substring(argumentPrefix.Length).Trim('"'));

        string environmentPath = Environment.GetEnvironmentVariable("COLORING_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(environmentPath)) return Path.GetFullPath(environmentPath);
        if (Path.IsPathRooted(relativeOutputDirectory)) return relativeOutputDirectory;

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string insideProject = Path.GetFullPath(Path.Combine(projectRoot, relativeOutputDirectory));
        if (Directory.Exists(insideProject)) return insideProject;

        string parentRoot = Directory.GetParent(projectRoot)?.FullName;
        if (!string.IsNullOrEmpty(parentRoot))
        {
            string besideProject = Path.GetFullPath(Path.Combine(parentRoot, relativeOutputDirectory));
            if (Directory.Exists(besideProject)) return besideProject;
        }

        // Convenience fallback for the current exhibition PC. This keeps the
        // built player connected to the existing photo pipeline without an
        // Inspector edit or a launcher script.
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string currentPipeline = Path.Combine(userProfile, "My project", "ImagePipeline", "output");
        return Directory.Exists(currentPipeline) ? currentPipeline : insideProject;
    }
}
