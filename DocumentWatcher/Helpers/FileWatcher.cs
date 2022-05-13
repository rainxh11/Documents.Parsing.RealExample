using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Globalization;
using System.Threading;

namespace DocumentWatcher.Helpers
{
    public class FileWatcher
    {
        public static FileSystemWatcher DocumentWatcher;
        public static IObservable<string> ChangingFiles;

        public static void Init()
        {
            var config = ConfigHelper.GetConfig();

            var documentFolder = new DirectoryInfo(config.DocumentFolder);
            if (!documentFolder.Exists)
            {
                documentFolder.Create();
            }

            DocumentWatcher = new FileSystemWatcher(config.DocumentFolder, config.ExtensionFilter);
            DocumentWatcher.IncludeSubdirectories = true;

            DocumentWatcher.NotifyFilter = NotifyFilters.LastAccess
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.CreationTime;

            DocumentWatcher.EnableRaisingEvents = true;

            ChangingFiles = Observable
                .FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                    h => DocumentWatcher.Changed += h,
                    h => DocumentWatcher.Changed -= h)
                .Where(x => x.EventArgs.ChangeType != WatcherChangeTypes.Created && x.EventArgs.ChangeType == WatcherChangeTypes.Changed)
                .Select(x => x.EventArgs.FullPath);


        }

        private static void ArchiveDocument(FileInfo file)
        {
            try
            {
                var dir = file.Directory.FullName + $@"\ARCHIVE\{file.CreationTime.ToString("yyyy-MM (MMMM)", CultureInfo.CreateSpecificCulture("fr-FR"))}\{file.CreationTime.ToString("yyyy-MM-dd (dd MMMM yyyy)", CultureInfo.CreateSpecificCulture("fr-FR"))}".ToUpperInvariant();

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.Move(file.FullName, dir + $@"\{file.Name}");
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.Message);
            }
        }

        private static (bool, FileInfo) IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return (true, file);
            }

            return (false, file);
        }

        public static void Start()
        {
            var config = ConfigHelper.GetConfig();
            var semaphore = new SemaphoreSlim(config.InitialConcurrentRefreshs, config.MaxConcurrentRefreshs);

            FileWatcher.ChangingFiles
                .Where(x => !x.Contains("$"))
                .Delay(TimeSpan.FromSeconds(5))
                .Subscribe(
                    async x =>
                    {
                        var file = new FileInfo(x);

                        try
                        {
                            await Task.Run(async () =>
                            {
                                while (IsFileLocked(file).Item1)
                                {
                                    //Console.WriteLine($"File '{file.Name}' is being used by another process, retyring in 5 seconds.");
                                    await Task.Delay(TimeSpan.FromSeconds(5));
                                }
                            });
                            //Console.WriteLine($"Saving changes of file '{file.Name}'");
                            await semaphore.WaitAsync();
                            await DocumentHelper.UpdateStudy(file);
                            semaphore.Release();
                        }
                        catch (Exception ex)
                        {
                            semaphore.Release();
                            //Console.WriteLine($"Error in saving file '{file.Name}', Exception Message: {Environment.NewLine}'{ex.Message}'");
                        }
                    }
                );

            Observable
                .Interval(TimeSpan.FromSeconds(10))
                .Repeat()
                .SelectMany(x => new DirectoryInfo(config.DocumentFolder).GetFiles("*.docx"))
                .Where(x => DateTime.Now > x.CreationTime && (DateTime.Now - x.CreationTime) >= new TimeSpan(24, 0, 0))
                .Where(x => !IsFileLocked(x).Item1)
                .Where(x => (DateTime.Now - x.LastAccessTime).Hours >= 1)
                .Do(async x => 
                {
                    await semaphore.WaitAsync();
                    ArchiveDocument(x);
                    semaphore.Release();
                })
                .Subscribe();

            Observable
                .Interval(TimeSpan.FromSeconds(5))
                .Repeat()
                .SelectMany(x => new DirectoryInfo(config.DocumentFolder).GetFiles("*.docx"))
                .Select(x => IsFileLocked(x))
                .Do(async x => 
                {
                    await semaphore.WaitAsync();
                    await DocumentHelper.ToggleStudy(x.Item2, x.Item1 ? "inProgress" : "complete");
                    semaphore.Release();

                })
                  .Subscribe();

            Observable
                .Interval(TimeSpan.FromSeconds(5))
                .Repeat()
                .SelectMany(x => new DirectoryInfo(config.DocumentFolder).GetFiles("*.docx", SearchOption.AllDirectories))
                .Where(x => !IsFileLocked(x).Item1)
                .Do(async x => 
                {
                    await semaphore.WaitAsync();
                    await DocumentHelper.CatchupStudy(x);
                    semaphore.Release();
                })
                .Subscribe();

        }
    }
}