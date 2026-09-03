using Puppeteer.Core;
namespace Puppeteer.Core.Tests;
public sealed class ProjectRulesTests
{
 [Theory][InlineData("node_modules")][InlineData(".git")][InlineData("obj")][InlineData(".venv")] public void GeneratedDirectoriesAreIgnored(string name)=>Assert.True(ProjectPathRules.IsIgnoredDirectory(Path.Combine("root",name)));
 [Fact] public void SourceDirectoryIsNotIgnored()=>Assert.False(ProjectPathRules.IsIgnoredDirectory(Path.Combine("root","src")));
 [Fact] public void InfersHierarchyWithoutProjectName(){var result=HierarchyInferer.Infer(Path.Combine("D:\\","Programming"),Path.Combine("D:\\","Programming","Desktop","Personal","Yoink"));Assert.Equal(["Desktop","Personal"],result);}
}
