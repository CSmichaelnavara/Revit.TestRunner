﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Revit.TestRunner.Shared.Communication;
using Revit.TestRunner.Shared.Communication.Client;
using Revit.TestRunner.Shared.Dto;
using Revit.TestRunner.Shared.Model;

namespace Revit.TestRunner.Shared
{
    public class TestRunnerClient
    {
        #region Members, Constructor

        private readonly FileClient mFileClient;
        private HomeDto mHome;
        private bool mNewRevit;

        /// <summary>
        /// Constructor
        /// </summary>
        public TestRunnerClient( string aClientName = "", string aClientVersion = "" )
        {
            mFileClient = new FileClient( FileNames.WatchDirectory, aClientName, aClientVersion );
        }

        #endregion

        #region Methods

        public async Task<ExploreResponseDto> ExploreLatestAssemblyAsync(string assemblyPath, string revitVersion,
            string revitLanguage, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(assemblyPath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Assembly DLL file not found", assemblyPath);
            var versionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
            string buildDir = fileInfo.DirectoryName;
            var versionDir = Path.Combine(buildDir + "Versions", versionInfo.FileVersion);
            var versionAssemblyPath = Path.Combine(versionDir, fileInfo.Name);
            if (!File.Exists(versionAssemblyPath))
            {
                Directory.CreateDirectory(versionDir);
                CopyFilesRecursively(buildDir, versionDir);
            }

            return await ExploreAssemblyAsync(versionAssemblyPath, revitVersion, revitLanguage, cancellationToken);

            /*Private Function SearchLastBuild(buildDir As String, dllFileName As String) As String
    buildDir = buildDir.TrimEnd("\")
    Dim dllFullFileName = System.IO.Path.Combine(buildDir, dllFileName)
    If Not File.Exists(dllFullFileName) Then Throw New FileNotFoundException("Dll file not found", dllFullFileName)

    Dim versionInfo = FileVersionInfo.GetVersionInfo(dllFullFileName)

    Dim versionDir = System.IO.Path.Combine(buildDir & "Versions", versionInfo.FileVersion)
    Dim versionDllFullFileName = System.IO.Path.Combine(versionDir, dllFileName)
    If File.Exists(versionDllFullFileName) Then Return versionDllFullFileName
    Directory.CreateDirectory(versionDir)
    CopyFilesRecursively(buildDir, versionDir)
    Return versionDllFullFileName

End Function*/
        }

        private void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            foreach (var dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            foreach (var sourceFileName in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(sourceFileName, sourceFileName.Replace(sourcePath, targetPath), true);
            }
            /*Private Sub CopyFilesRecursively(sourcePath As String, targetPath As String)
    'Now Create all of the directories
    For Each dirPath As String In Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)
        Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath))
    Next
    'Copy all the files & Replaces any files with the same name
    For Each newPath As String In Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories)
        File.Copy(newPath, newPath.Replace(sourcePath, targetPath), True)
    Next
End Sub
*/
        }

        /// <summary>
        /// Start a explore request.
        /// </summary>
        public async Task<ExploreResponseDto> ExploreAssemblyAsync( string assemblyPath, string revitVersion, string revitLanguage, CancellationToken cancellationToken )
        {
            ExploreResponseDto result = null;

            var request = new ExploreRequestDto {
                AssemblyPath = assemblyPath
            };

            var revit = RevitHelper.StartRevit( revitVersion, revitLanguage );
            mNewRevit |= revit.IsNew;

            await GetHome( cancellationToken );

            if( mHome != null ) {
                result = await mFileClient.GetJson<ExploreRequestDto, ExploreResponseDto>( mHome.ExplorePath, request, cancellationToken );
            }
            else {
                Console.WriteLine( "TestRunner service not available!" );
            }

            return result;
        }

        /// <summary>
        /// Call GetHome
        /// </summary>
        private async Task<HomeDto> GetHome( CancellationToken cancellationToken )
        {
            mHome = await mFileClient.GetJson<HomeDto>( "", cancellationToken, 30, 2000 );
            return mHome;
        }

        /// <summary>
        /// Try to get latest test result of <paramref name="aAssemblyName"/>.
        /// </summary>
        public TestRunStateDto GetNewestTestResult( string aAssemblyName )
        {
            if( string.IsNullOrEmpty( aAssemblyName ) ) return null;
            if( string.IsNullOrEmpty( mHome?.TestPath ) ) return null;
            if( !Directory.Exists( mHome.TestPath ) ) return null;

            var resultFiles = Directory.GetFiles( mHome.TestPath, "result.json", SearchOption.AllDirectories )
                .OrderByDescending( path => path );

            foreach( var resultFile in resultFiles ) {
                var loadedResult = JsonHelper.FromFile<TestRunStateDto>( resultFile );
                if( loadedResult?.Cases == null ) continue;

                if( loadedResult.Cases.Any( c => c.AssemblyPath.Contains( aAssemblyName ) ) ) {
                    return loadedResult;
                }
            }

            return null;
        }

        /// <summary>
        /// Start loop of calling home. 
        /// </summary>
        public void StartRunnerStatusWatcher( Action<HomeDto> aCallback, CancellationToken cancellationToken )
        {
            Task.Run( async () => {
                while( !cancellationToken.IsCancellationRequested && mHome == null ) {
                    mHome = await GetHome( cancellationToken );
                    aCallback( mHome );

                    Thread.Sleep( 1000 );
                }
            }, cancellationToken );
        }

        /// <summary>
        /// Start a test run request.
        /// </summary>
        public async Task StartTestRunAsync( IEnumerable<TestCaseDto> testCases, string revitVersion, string revitLanguage, Action<TestRunState> callback, CancellationToken cancellationToken )
        {
            var request = new TestRequestDto {
                Cases = testCases.ToArray()
            };

            var (processId, isNew) = RevitHelper.StartRevit( revitVersion, revitLanguage );
            mNewRevit |= isNew;

            await GetHome( cancellationToken );

            if( mHome != null ) {
                var response = await mFileClient.GetJson<TestRequestDto, TestResponseDto>( mHome.TestPath, request, cancellationToken );

                if( response != null ) {
                    var resultFile = response.ResultFile;

                    // Wait resultFile 
                    for( int i = 0; i < 10; i++ )
                        if( !File.Exists( resultFile ) ) {
                            Console.WriteLine( "." ); // Wait resultFile
                            await Task.Delay( 200, cancellationToken );
                        }

                    if( File.Exists( resultFile ) ) {
                        bool run = true;

                        while( run && !cancellationToken.IsCancellationRequested ) {
                            var runResult = JsonHelper.FromFile<TestRunStateDto>( resultFile );

                            if( runResult != null ) {
                                bool isCompleted = runResult.State == TestState.Passed || runResult.State == TestState.Failed;
                                var result = new TestRunState( runResult, isCompleted ) { Message = runResult.Output };

                                callback( result );

                                run = !isCompleted;

                                if( run ) await Task.Delay( 500, cancellationToken );
                            }
                        }
                    }
                    else {
                        callback( new TestRunState( null, true ) { Message = "Tests not executed! Service may not be running." } );
                    }

                    if( mNewRevit ) RevitHelper.KillRevit( processId );
                }
            }
            else {
                callback( new TestRunState( null, true ) { Message = "TimeOut. Runner not available!" } );
            }
        }

        #endregion
    }
}