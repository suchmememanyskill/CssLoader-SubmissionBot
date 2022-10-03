using System.IO.Compression;
using Discord;
using Discord.Interactions;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SubmissionBot.Modules.Base;
using SubmissionBot.Utils;

namespace SubmissionBot.Modules.SlashCommands;

public enum ChecklistAcceptOptions
{
    [ChoiceDisplay("I Accept all items on the checklist")]
    Accept,
    [ChoiceDisplay("I cannot check some items on the checklist")]
    Deny,
    [ChoiceDisplay("Where can i find the checklist?")]
    NotRead,
}

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
    }

    private void Log(string message)
    {
        Console.WriteLine($"[CSS] {message}");
    }
    
    [SlashCommand("submit", "Submits a CSS Theme. This will override any submission you currently have open")]
    public async Task Submit(IAttachment theme,
        [Summary(description: "Is this theme in line with our checklist?")] ChecklistAcceptOptions checklist,
        [Summary(description: "How does your theme bundle other themes, if it does?")] ThemeBundleOptions themeBundle,
        [Summary(description: "If your theme themes the keyboard, how does it do it?")] ThemeTypeOptions keyboardBehaviour)
    {
        if (checklist == ChecklistAcceptOptions.NotRead)
        {
            await me.RespondEphermeral(
                "The checklist can be found here: <https://github.com/suchmememanyskill/CssLoader-ThemeDb/blob/main/.github/pull_request_template.md>");
            return;
        }

        if (checklist == ChecklistAcceptOptions.Deny)
        {
            await me.RespondEphermeral(
                "Please contact one of the CSSLoader ThemeDB Admins. All items on the checklist need to be checked for a successful submission");
            return;
        }

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
        
        string themeDbDir = GetTemporaryDirectory();
        Repository.Clone("https://github.com/suchmememanyskill/CssLoader-ThemeDb", themeDbDir);
        
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

        await FollowupAsync("Theme looks good. Done for today");
        
        
        
        CleanupTemporaryDirectories();
    }
}