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
 [Fact] public async Task SolutionIncludesNestedLanguageAndActualFrameworkSettings()
 {
  var path=CreateTemp();
  try
  {
   await File.WriteAllTextAsync(Path.Combine(path,"App.sln"),"");
   var app=Directory.CreateDirectory(Path.Combine(path,"src","App")).FullName;
   await File.WriteAllTextAsync(Path.Combine(app,"App.csproj"),"<Project><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>");
   var result=await new SignatureProjectDetector().DetectAsync(path);
   Assert.Contains("C#",result!.Technologies); Assert.Contains("WPF",result.Technologies);
   Assert.DoesNotContain("Windows Forms",result.Technologies);
  }
  finally{Directory.Delete(path,true);}
 }
 [Fact] public async Task TauriIncludesSourceLanguagesButIgnoresBuildAndDependencyFiles()
 {
  var path=CreateTemp();
  try
  {
   Directory.CreateDirectory(Path.Combine(path,"src-tauri"));
   await File.WriteAllTextAsync(Path.Combine(path,"src-tauri","tauri.conf.json"),"{}");
   var source=Directory.CreateDirectory(Path.Combine(path,"src")).FullName;
   foreach(var file in new[]{"index.html","style.css","app.ts"}) await File.WriteAllTextAsync(Path.Combine(source,file),"");
   foreach(var folder in new[]{"node_modules","target","obj"})
   {
    Directory.CreateDirectory(Path.Combine(path,folder));
    await File.WriteAllTextAsync(Path.Combine(path,folder,"generated.py"),"");
   }
   await File.WriteAllTextAsync(Path.Combine(path,"package.json"),"{}");
   var result=await new SignatureProjectDetector().DetectAsync(path);
   foreach(var technology in new[]{"Tauri","Rust","HTML","CSS","TypeScript"}) Assert.Contains(technology,result!.Technologies);
   Assert.DoesNotContain("Python",result!.Technologies);
  }
  finally{Directory.Delete(path,true);}
 }
 [Fact] public async Task PackageDescriptionsDoNotCountAsDependencies()
 {
  var path=CreateTemp();
  try
  {
   await File.WriteAllTextAsync(Path.Combine(path,"package.json"),"""{"description":"react typescript","dependencies":{"vue":"latest","tailwindcss":"latest"}}""");
   var result=await new SignatureProjectDetector().DetectAsync(path);
   Assert.Contains("Vue",result!.Technologies); Assert.Contains("Tailwind",result.Technologies);
   Assert.DoesNotContain("React",result.Technologies); Assert.DoesNotContain("TypeScript",result.Technologies);
  }
  finally{Directory.Delete(path,true);}
 }
 private static string CreateTemp(){var path=Path.Combine(Path.GetTempPath(),"puppeteer-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
}
