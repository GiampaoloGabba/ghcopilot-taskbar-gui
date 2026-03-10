using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotTaskbarApp;

public enum AiProvider
{
    GitHubCopilot,
    ClaudeCode
}

public class AiProviderSettings
{
    public AiProvider ActiveProvider { get; set; } = AiProvider.GitHubCopilot;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotTaskbarApp",
        "settings.json");

    public static AiProviderSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AiProviderSettings>(json) ?? new AiProviderSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiProviderSettings] Failed to load: {ex.Message}");
        }
        return new AiProviderSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiProviderSettings] Failed to save: {ex.Message}");
        }
    }
}
