using Puppeteer.Core;
namespace Puppeteer.Core.Tests;
public sealed class ProjectSearchTests
{
 private static readonly Project Mobile=new(Guid.NewGuid(),"PharmaTouch","D:/Programming/Mobile/Personal/PharmaTouch",Guid.NewGuid(),"Flutter",["Flutter","Dart"],["Mobile","Personal"],[],Category:"Personal");
 private static readonly Project Desktop=new(Guid.NewGuid(),"Yoink","D:/Programming/Desktop/Clients/Yoink",Guid.NewGuid(),"Tauri",["Tauri","Rust"],["Desktop","Clients"],[],Category:"Client");
 [Fact] public void CombinesMetadataTerms(){var result=new ProjectSearchService().Filter([Mobile,Desktop],"flutter personal");Assert.Single(result);Assert.Equal("PharmaTouch",result[0].Name);}
 [Fact] public void TypeFilterMatchesByStack(){var result=new ProjectSearchService().Filter([Mobile,Desktop],null,type:"Desktop");Assert.Single(result);Assert.Equal("Yoink",result[0].Name);}
 [Fact] public void TechnologyFilterMatchesExactTech(){var result=new ProjectSearchService().Filter([Mobile,Desktop],null,technology:"Rust");Assert.Single(result);Assert.Equal("Yoink",result[0].Name);}
 [Fact] public void CategoryFilterMatchesStoredCategory(){var result=new ProjectSearchService().Filter([Mobile,Desktop],null,category:"Client");Assert.Single(result);Assert.Equal("Yoink",result[0].Name);}
 [Fact] public void RunningKeywordUsesSessionIndex(){var result=new ProjectSearchService().Filter([Mobile,Desktop],"running mobile",runningProjectIds:new HashSet<Guid>{Mobile.Id});Assert.Single(result);Assert.Equal(Mobile.Id,result[0].Id);}
 [Fact] public void BuildTypesReturnsPresentProductTypes()
 {
  var projects=new[]{
   new Project(Guid.NewGuid(),"A","/root/seg1/A",Guid.NewGuid(),"Vite",["Vite","Node.js"],["seg1"],[]),
   new Project(Guid.NewGuid(),"B","/root/seg2/B",Guid.NewGuid(),"Tauri",["Tauri","Rust"],["seg2"],[]),
   new Project(Guid.NewGuid(),"C","/root/seg3/C",Guid.NewGuid(),"Flutter",["Flutter"],["seg3"],[])};
  Assert.Equal(["All","Desktop","Mobile","Website"],new ProjectSearchService().BuildTypes(projects));
 }
 [Fact] public void PathCategoryHeuristicReadsFolderNames()
 {
  Assert.Equal("Client",ProjectCategoryRules.FromPath("D:/dev/Clients/Acme"));
  Assert.Equal("Course",ProjectCategoryRules.FromPath("D:/dev/School/QT/Academia"));
  Assert.Null(ProjectCategoryRules.FromPath("D:/dev/Tauri/Yoink"));
 }
}
