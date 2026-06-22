using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

public class ApplicationManifestTests
{
    [Fact]
    public void Project_embeds_dpi_unaware_application_manifest()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "UnimesAutomation", "UnimesAutomation.csproj");
        var manifestPath = Path.Combine(root, "src", "UnimesAutomation", "app.manifest");

        Assert.True(File.Exists(manifestPath), "app.manifest should exist next to the application project.");

        var project = XDocument.Load(projectPath);
        var manifestReference = project.Descendants("ApplicationManifest").SingleOrDefault()?.Value;
        Assert.Equal("app.manifest", manifestReference);

        var manifest = XDocument.Load(manifestPath);
        var dpiAware = manifest.Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "dpiAware")
            ?.Value;
        var dpiAwareness = manifest.Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "dpiAwareness")
            ?.Value;

        Assert.Equal("false", dpiAware);
        Assert.Equal("unaware", dpiAwareness);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "appsettings.example.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "UnimesAutomation")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
