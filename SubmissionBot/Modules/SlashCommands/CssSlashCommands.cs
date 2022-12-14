using System.IO.Compression;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SubmissionBot.Modules.Base;
using SubmissionBot.Utils;

namespace SubmissionBot.Modules.SlashCommands;

public enum ThemeBundleOptions
{
    [ChoiceDisplay("This theme does not bundle other themes")]
    NotBundled,
    [ChoiceDisplay("This theme bundles other themes as toggleable items")]
    ToggleableBundled,
    [ChoiceDisplay("This theme bundles other themes without them being toggleable")]
    Bundled,
}

public enum ThemeTypeOptions
{
    [ChoiceDisplay("This theme is a keyboard Theme and is applied to the default keyboard")]
    KeyboardDefaultKeyboard,
    [ChoiceDisplay("This theme is a keyboard Theme but is NOT applied to the default keyboard")]
    KeyboardNonDefaultKeyboard,
    [ChoiceDisplay("This theme includes a keyboard theme and is toggleable")]
    SystemWideToggleableKeyboard,
    [ChoiceDisplay("This theme includes a keyboard theme but is NOT toggleable")]
    SystemWideNonToggleableKeyboard,
    [ChoiceDisplay("This theme does not theme the keyboard")]
    NoKeyboard,
}

public class ThemeDbEntry
{
    [JsonProperty("repo_url")] 
    public string RepoUrl { get; set; }
    [JsonProperty("repo_subpath")] 
    public string RepoSubpath { get; set; }
    [JsonProperty("repo_commit")] 
    public string RepoCommit { get; set; } 
    [JsonProperty("preview_image_path")] 
    public string PreviewImagePath { get; set; }

    public ThemeDbEntry(string repoUrl, string repoSubpath, string repoCommit, string previewImagePath)
    {
        RepoUrl = repoUrl;
        RepoSubpath = repoSubpath;
        RepoCommit = repoCommit;
        PreviewImagePath = previewImagePath;
    }
}

public record ThemeEntry(string Name, string Author);

[Group("css", "Submit CSS themes to the CSS ThemeDB")]
public class CssSlashCommands : SlashCommandBase
{
    private List<string> _tempDirs = new();
    public IConfiguration Config { get; set; }
    
    private string GetTemporaryDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        _tempDirs.Add(tempDirectory);
        return tempDirectory;
    }

    private void CleanupTemporaryDirectories()
    {
        foreach (var tempDir in _tempDirs)
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch (Exception e)
            {
                Log($"Failed to delete '{tempDir}', {e.Message}");
            }
        }

        _tempDirs = new();
    }

    private void Log(string message)
    {
        Console.WriteLine($"[CSS] {message}");
    }
    
    [SlashCommand("submit", "Submits a CSS Theme. This will override any submission you currently have open")]
    public async Task Submit([Summary(description: "A .zip containing the theme. theme.json needs to be on the root level")] IAttachment theme,
        [Summary(description: "How does your theme bundle other themes, if it does?")] ThemeBundleOptions themeBundle,
        [Summary(description: "If your theme themes the keyboard, how does it do it?")] ThemeTypeOptions keyboardBehaviour)
    {
        if (themeBundle == ThemeBundleOptions.Bundled)
        {
            await me.RespondEphermeral(
                "If you want to submit a theme that bundles other themes, they need to be toggleable. This is to encourage mixing and matching themes. Please change your theme accordingly. If you don't know how to do this, contact one of the CSSLoader ThemeDB Admins");
            return;
        }

        if (keyboardBehaviour == ThemeTypeOptions.KeyboardNonDefaultKeyboard)
        {
            await me.RespondEphermeral(
                "If you want to submit a keyboard theme, it needs to target the default keyboard. If you don't know how to do this, contact one of the CSSLoader ThemeDB Admins");
            return;
        }
        
        if (keyboardBehaviour == ThemeTypeOptions.SystemWideNonToggleableKeyboard)
        {
            await me.RespondEphermeral(
                "If you want to submit a theme that also targets the keyboard, it needs to be toggleable. If you don't know how to do this, contact one of the CSSLoader ThemeDB Admins");
            return;
        }

        if (theme.Size > 0x400000)
        {
            await me.RespondEphermeral("Theme is too big. Themes can be max 4MB");
            return;
        }
        
        Log($"All provided enums look valid, and zip is roughly the right size\nSubmission by {Context.User.Username} ({Context.User.Id})");

        await DeferAsync();

        string themeDir = GetTemporaryDirectory();
        string themeZipPath = Path.Join(themeDir, "theme.zip");

        using (HttpClient client = new())
        {
            var request = await client.GetAsync(theme.Url);
            await File.WriteAllBytesAsync(themeZipPath, await request.Content.ReadAsByteArrayAsync());
        }
        
        Log("Downloaded theme zip");

        long totalLength = 0;

        try
        {
            using (ZipArchive archive = ZipFile.OpenRead(themeZipPath))
            {
                foreach (var zipArchiveEntry in archive.Entries)
                {
                    totalLength += zipArchiveEntry.Length;

                    if (totalLength > 0x400000)
                    {
                        throw new Exception("Theme is too big. Themes can be max 4MB");
                    }
                }
            }
        }
        catch (InvalidDataException e)
        {
            await FollowupAsync("Uploaded file is not a zip", allowedMentions: AllowedMentions.None);
            CleanupTemporaryDirectories();
            return;
        }
        catch (Exception e)
        {
            await FollowupAsync(e.Message, allowedMentions: AllowedMentions.None);
            CleanupTemporaryDirectories();
            return;
        }
        
        Log($"Total extracted size will be {totalLength} bytes");

        string fullThemePath = Path.Join(themeDir, "theme");
        Directory.CreateDirectory(fullThemePath);
        ZipFile.ExtractToDirectory(themeZipPath, fullThemePath);
        
        Log($"Extracted zip into '{fullThemePath}'");

        if (!File.Exists(Path.Join(fullThemePath, "thumbnail.jpg")))
        {
            await FollowupAsync("Zip does not contain a `thumbnail.jpg` image");
            CleanupTemporaryDirectories();
            return;
        }

        string thumbnailPath = Path.Join(themeDir, "thumbnail.jpg");
        File.Move(Path.Join(fullThemePath, "thumbnail.jpg"), thumbnailPath);
        
        string themeDbDir = GetTemporaryDirectory();
        await Git.Clone("https://github.com/suchmememanyskill/CssLoader-ThemeDb", themeDbDir);
        
        Log($"Cloned ThemeDB repo into {themeDbDir}");

        string themeDbThemesPath = Path.Join(themeDbDir, "themes");
        Directory.Delete(themeDbThemesPath, true);
        Directory.CreateDirectory(themeDbThemesPath);
        await File.WriteAllTextAsync(Path.Join(themeDbThemesPath, "theme.json"), JsonConvert.SerializeObject(new ThemeDbEntry("LOCAL", fullThemePath, "abcdef",
            "images/SuchMeme/ColoredToggles.jpg")));
        
        Log($"Created .json entry, ready for validation");
        
        Terminal t = new();
        t.WorkingDirectory = themeDbDir;
        if (!await t.Exec("python3", "main.py"))
        {
            await FollowupAsync("Python failed?");
            CleanupTemporaryDirectories();
            return;
        }

        if (t.ExitCode != 0)
        {
            await FollowupAsync($"Submission failed to validate\n{t.StdErr.Last()}", allowedMentions: AllowedMentions.None);
            CleanupTemporaryDirectories();
            return;
        }

        ThemeEntry themeEntry =
            JsonConvert.DeserializeObject<ThemeEntry>(
                await File.ReadAllTextAsync(Path.Join(fullThemePath, "theme.json")))!;

        Log($"Theme looks good. Theme name is {themeEntry.Name}");

        Git themeStorage = new(Config["cssRepoPath"]);
        await themeStorage.ResetHard("HEAD");
        await themeStorage.Clean();
        await themeStorage.Pull();

        string finalThemeDirName = $"CSS-{Context.User.Id}-{themeEntry.Name}";
        string finalThemePath = Path.Join(themeStorage.Path, finalThemeDirName);
        
        if (Directory.Exists(finalThemePath))
            Directory.Delete(finalThemePath, true);

        Directory.CreateDirectory(finalThemePath);
        CopyFilesRecursively(fullThemePath, finalThemePath);

        await themeStorage.Add(".");

        int filesChangedCount = await themeStorage.GetStagedFileCount();

        if (filesChangedCount <= 0)
        {
            await FollowupAsync("No files have changed since the last upload");
            CleanupTemporaryDirectories();
            return;
        }

        await themeStorage.Commit($"Adding/Updating {themeEntry.Name}");
        await themeStorage.Push();
        
        Log("Pushed theme to github");

        Git themeDb = new(Config["cssDbRepoPath"]);
        await themeDb.Checkout("main");
        await themeDb.Fetch("upstream");
        await themeDb.Clean();
        await themeDb.ResetHard("upstream/main");

        await themeDb.CreateBranch(Context.User.Id.ToString());
        await themeDb.Checkout(Context.User.Id.ToString());

        string relativeImagePath = $"images/{themeEntry.Author}/{themeEntry.Name}.jpg";
        string imagePathDir = Path.Join(themeDb.Path, $"images/{themeEntry.Author}");

        if (!Directory.Exists(imagePathDir))
            Directory.CreateDirectory(imagePathDir);
        
        File.Copy(thumbnailPath, Path.Join(imagePathDir, $"{themeEntry.Name}.jpg"));

        ThemeDbEntry entry = new("https://github.com/SuchMeme-Bot/CssLoader-Themes", finalThemeDirName,
            await themeStorage.GetLatestCommitHash(), relativeImagePath);

        await File.WriteAllTextAsync(
            Path.Join(themeDb.Path, "themes", $"{themeEntry.Author}-{themeEntry.Name}.json"),
            JsonConvert.SerializeObject(entry));

        await themeDb.Add(".");
        await themeDb.Commit($"Submit theme {themeEntry.Name}");
        await themeDb.Push(Context.User.Id.ToString(), true);

        Log("Pushed .json entry to ThemeDB");

        if (!await themeDb.DoesPullRequestExist(Context.User.Id.ToString()))
        {
            string title = $"Submit Theme {themeEntry.Name}";

            string bundledState = (themeBundle == ThemeBundleOptions.ToggleableBundled)
                ? "This theme bundles other themes (which are toggleable)"
                : "This theme does not bundle other themes";

            string keyboardState = (keyboardBehaviour == ThemeTypeOptions.SystemWideToggleableKeyboard)
                ?
                "This theme bundles a keyboard theme (which is toggleable)"
                : (keyboardBehaviour == ThemeTypeOptions.KeyboardDefaultKeyboard)
                    ? "This theme is a keyboard theme and targets the default keyboard"
                    : "This theme does not touch the keyboard";
            
            string body = $"This pull request was submitted by a bot\nSubmitted by {Context.User.Username} ({themeEntry.Author} in theme)\n\nUser accepted the pull request checklist\n{bundledState}\n{keyboardState}\nURL: [{themeEntry.Name}](https://github.com/SuchMeme-Bot/CssLoader-Themes/tree/main/{finalThemeDirName.Replace(" ", "%20")})";
            string url = await themeDb.CreatePullRequest(title, body);
            await FollowupAsync($"Successfully submitted theme\n{url}");
            Log($"Created pull request @ {url}");
        }
        else
        {
            Log("Pull request already exists, skipping...");
            await FollowupAsync("Successfully updated theme submission");
        }
        
        CleanupTemporaryDirectories();
    }
    
    ~CssSlashCommands()
    {
        Log("Deconstructing class");
        CleanupTemporaryDirectories();
    }
    
    private static void CopyFilesRecursively(string sourcePath, string targetPath)
    {
        //Now Create all of the directories
        foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
        }

        //Copy all the files & Replaces any files with the same name
        foreach (string newPath in Directory.GetFiles(sourcePath, "*.*",SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
        }
    }
}