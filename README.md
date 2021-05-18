# AntVoiceBuild (from ssergi IncrementalBuild)

Build Process to only build and deploy changed projects in a single repository environment.

# Deploy AntVoiceBuild on NuGet or locally

You can try to build and deploy the build library in any NuGet repository. To so so you can use the command :

`BUILD_NUMBER=20210518.1 NUGET_SOURCE=https://your-repo.example.com/repository/nuget-hosted/ NUGET_KEY=my-nuget-repo-key dotnet fake run build.fsx -- -t Nuget-publish`

Or if you only want to check the nupkg generated :

`BUILD_NUMBER=20210518.x OUTPUT_PATH=/my/path/ dotnet fake run build.fsx -- -t Nuget-publish-local`

