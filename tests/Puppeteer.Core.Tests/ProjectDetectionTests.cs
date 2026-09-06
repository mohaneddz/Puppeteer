using Puppeteer.Core;
namespace Puppeteer.Core.Tests;
public sealed class ProjectDetectionTests
{
 [Fact] public async Task DetectsTauriAndFrontendStack()
 {
  var path=CreateTemp();try{Directory.CreateDirectory(Path.Combine(path,"src-tauri"));await File.WriteAllTextAsync(Path.Combine(path,"src-tauri","tauri.conf.json"),"{}");await File.WriteAllTextAsync(Path.Combine(path,"package.json"),"""{"dependencies":{"react":"latest"},"devDependencies":{"typescript":"latest"}}""");var result=await new SignatureProjectDetector().DetectAsync(path);Assert.NotNull(result);Assert.Equal("Tauri",result.PrimaryTechnology);Assert.Contains("Rust",result.Technologies);Assert.Contains("React",result.Technologies);Assert.Contains("TypeScript",result.Technologies);}finally{Directory.Delete(path,true);}
 }
 [Fact] public async Task DetectsDotNetCommands(){var path=CreateTemp();try{await File.WriteAllTextAsync(Path.Combine(path,"App.csproj"),"<Project />");var result=await new SignatureProjectDetector().DetectAsync(path);Assert.Equal(".NET",result!.PrimaryTechnology);Assert.Contains(result.Presets,x=>x.Command=="dotnet build");}finally{Directory.Delete(path,true);}}
 [Fact] public async Task DetectsDotNetSolutionXmlFormat(){var path=CreateTemp();try{await File.WriteAllTextAsync(Path.Combine(path,"App.slnx"),"<Solution />");var result=await new SignatureProjectDetector().DetectAsync(path);Assert.Equal(".NET",result!.PrimaryTechnology);Assert.Contains(result.Presets,x=>x.Command=="dotnet build");}finally{Directory.Delete(path,true);}}
 private static string CreateTemp(){var path=Path.Combine(Path.GetTempPath(),"puppeteer-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
}
