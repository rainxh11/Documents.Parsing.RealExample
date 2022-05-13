using DocumentWatcher.Helpers;
using DocumentWatcher.Models;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using MongoDB.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentWatcher.Controllers
{
    public class OpenDicomController : WebApiController
    {
        [Route(HttpVerbs.Post, "/opendicom/{id}")]
        public async Task<string> OpenDicom(string id)
        {
            try
            {
                var config = ConfigHelper.GetConfig();
                var response = await Program.dicomServerApi.DownloadStudyArchive(id);
                var archive = response.Content.ReadAsStream();

                var destinationDir = new DirectoryInfo(config.DicomTemp);
                if(!destinationDir.Exists) destinationDir.Create();
                var file = new FileInfo(destinationDir.FullName + $@"\{id}.zip");

                if (!file.Exists)
                {
                    using (var fileStream = new FileStream(file.FullName, FileMode.CreateNew))
                    {
                        byte[] buffer = new byte[8 * 1024];
                        int len;
                        while((len = archive.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fileStream.Write(buffer, 0, len);
                        }
                        fileStream.Close();    
                    }
                }
                var dicomViewer = DicomViewerHelper.GetDicomViewer(config.DicomViewer);
                var viewer = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = file.FullName,
                        Arguments = $"\"{file.FullName}\""
                    }
                };
                viewer.Start();

                return $"Success, Opened Study: {id}";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
