namespace VersionOne.Web.PackagerManager

open System
open NuGet
open System.Collections.Generic
open System.Linq
open System.IO

type PackageModel = {
    Id : String
    Version : String
    Description : String
    IsInstalled : bool
    ReleaseNotes : String
    Authors : String
}

type PackageManager(packageFolder, installFolder, packageSearchPattern, packageManagerRepositoryUrl
, additionalPackageRepositoryUrls : IEnumerable<string>) =
    let installMarker packageId packageVersion = installFolder + "\\" + packageId + "." + packageVersion + ".installed"
    let mainPackageRepository = NuGet.PackageRepositoryFactory.Default.CreateRepository(packageManagerRepositoryUrl)
    let mainPackageManager = NuGet.PackageManager(mainPackageRepository, packageFolder)

    let additionalPackageRepositories = [
        for packageRepositoryUrl in additionalPackageRepositoryUrls -> NuGet.PackageRepositoryFactory.Default.CreateRepository(packageRepositoryUrl)
    ]
        
    let allRepositories = additionalPackageRepositories.Union([mainPackageRepository])

    let packageRepositories = NuGet.AggregateRepository(allRepositories)
    let packageManager = NuGet.PackageManager(packageRepositories, packageFolder)

    let isInstalledInInstallFolder packageId packageVersion =
        File.Exists(installFolder + "\\" + packageId + ".dll") && File.Exists(installMarker packageId packageVersion)
               
    let copyModuleToInstallFolder packageId packageVersion = 
        let searchPattern = packageId + ".dll"
        let moduleFolder = DirectoryInfo (packageFolder + "\\" + packageId + "." + packageVersion)
        for file in moduleFolder.GetFiles(searchPattern, SearchOption.AllDirectories) do
            let destinationFile = (installFolder + "\\" + file.Name)
            file.CopyTo(destinationFile, true) |> ignore
            File.Create(installMarker packageId packageVersion).Dispose()

    let deletePreviousInstallMarkers packageId = 
        let searchPattern = packageId + "*.installed"
        let installFolderFiles = DirectoryInfo(installFolder)
        for file in installFolderFiles.GetFiles(searchPattern) do
            file.Delete()

    let deleteModuleFromInstallFolder packageId packageVersion =        
        let searchPattern = packageId + ".dll"        
        let installFiles = DirectoryInfo installFolder
        for file in installFiles.GetFiles searchPattern do 
            file.Delete()
            File.Delete(installMarker packageId packageVersion)
        ()

    member x.ListPackages () =
        let allPackages = [ 
            for repoPackage in mainPackageManager.SourceRepository.GetPackages() ->
                { 
                    Id = repoPackage.Id;
                    Version = repoPackage.Version.ToString();
                    Description = repoPackage.Description;
                    IsInstalled = (isInstalledInInstallFolder repoPackage.Id (repoPackage.Version.ToString()) );
                    ReleaseNotes = repoPackage.ReleaseNotes;
                    Authors = String.Join("",repoPackage.Authors.ToArray())
                }
        ]
        let groups = query {
            for p in allPackages do
            groupBy p.Id
        }        
        let groupedByIdSortedByVersion = Dictionary<String, IOrderedEnumerable<PackageModel>>()
        for group in groups do            
            groupedByIdSortedByVersion.Add(group.Key, group.ToList().OrderByDescending(fun g -> g.Version))
        groupedByIdSortedByVersion

    member x.ListLatestTranslatorPlugins () =
        let allPackages = [ 
            for repoPackage in mainPackageManager.SourceRepository.Search(packageSearchPattern, false) ->
                { 
                    Id = repoPackage.Id;
                    Version = repoPackage.Version.ToString();
                    Description = repoPackage.Description;
                    IsInstalled = (isInstalledInInstallFolder repoPackage.Id (repoPackage.Version.ToString()) );
                    ReleaseNotes = repoPackage.ReleaseNotes;
                    Authors = String.Join("",repoPackage.Authors.ToArray())
                }
        ]
        let groups = query {
            for p in allPackages do
            groupBy p.Id
        }        
        let groupedByIdOnlyLatestVersion = Dictionary<String, PackageModel>()
        for group in groups do            
            groupedByIdOnlyLatestVersion.Add(group.Key, group.ToList().OrderByDescending(fun g -> g.Version).First())
        groupedByIdOnlyLatestVersion

    member x.Install packageId (packageVersion : String) =
        packageManager.InstallPackage(packageId, SemanticVersion(packageVersion))
        deletePreviousInstallMarkers packageId
        copyModuleToInstallFolder packageId packageVersion
        true

    member x.Uninstall packageId (packageVersion : String) =
        packageManager.UninstallPackage(packageId, SemanticVersion(packageVersion))
        deleteModuleFromInstallFolder packageId packageVersion
        true

    member x.InstallLatestTranslatorPlugins () =
        let translatorPlugins = x.ListLatestTranslatorPlugins()
        for key in translatorPlugins.Keys do
            let plugin = translatorPlugins.[key]
            if plugin.IsInstalled <> true then
                x.Install plugin.Id plugin.Version |> ignore