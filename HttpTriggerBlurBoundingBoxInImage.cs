//#r "Microsoft.WindowsAzure.Storage"

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using System.Net;
using System.Net.Http;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Blurring.Function
{
    public static class HttpTriggerBlurBoundingBoxInImage
    {
        [FunctionName("HttpTriggerBlurBoundingBoxInImage")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function,"post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            //Set up link to blob storage for stored images
            string storageConnectionString = "<YOUR STORAGE CONNECTION STRING HERE>";
            CloudStorageAccount blobAccount = CloudStorageAccount.Parse(storageConnectionString);
            CloudBlobClient blobClient = blobAccount.CreateCloudBlobClient();

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if(!string.IsNullOrEmpty(requestBody))
            {
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                string sourceName = data.sourceName;
                string sourceContainer = data.sourceContainer;
                string destinationName = data.destinationName;
                string destinationContainer = data.destinationContainer;
                string probability = data.probability;
                int i = 0;

                if (!string.IsNullOrEmpty(sourceName))
                {
                    //Get reference to specific car's container from Car Reg (converts to lower case as container names must be lower case)
                    CloudBlobContainer blobContainer = blobClient.GetContainerReference(sourceContainer.ToLower());
                    //Get reference to image block blob image from ImageFileName parameter the user passed in (images must be in jpg format in the blob service for this to work)
                    CloudBlockBlob cloudBlockBlob = blobContainer.GetBlockBlobReference(sourceName.ToLower());
                    //Download the image to a stream
                    using (var inputStream = await cloudBlockBlob.OpenReadAsync().ConfigureAwait(false))
                    {
                        using (var image = Image.Load(inputStream))
                        {  
                            //For each boundingbox, blur the rectangle
                            foreach (var bb in data.boundingBox)
                            {
                                i+=1;
                                int left = bb.left;
                                int top = bb.top;
                                int width = bb.width;
                                int height = bb.height;
                                image.Mutate(x => x.GaussianBlur(20, new Rectangle(left,top,width,height)));
                                log.LogInformation("Object " + i + " blurred.");
                            }
                            log.LogInformation("Finished image.");
                            
                            //Upload the stream to Blob Storage
                            byte[] arr;
                            using (MemoryStream streamOut = new MemoryStream())
                            {
                                image.SaveAsJpeg(streamOut);
                                arr = streamOut.GetBuffer();

                                CloudBlobContainer cloudBlobContainerDest = blobClient.GetContainerReference(destinationContainer.ToLower());
                                await cloudBlobContainerDest.CreateIfNotExistsAsync();                             
                                CloudBlockBlob cloudBlockBlobDest = cloudBlobContainerDest.GetBlockBlobReference(destinationName); 
                                streamOut.Seek(0, SeekOrigin.Begin);                              
                                //Task.WaitAll(cloudBlockBlobDest.UploadFromStreamAsync(streamOut));
                                await cloudBlockBlobDest.UploadFromStreamAsync(streamOut);

                            }
                            //Create the Http response message with the blurred image
                            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
                            return (ActionResult)new OkObjectResult("Uploaded to Blob storage");
                        }
                    }
                }
            
                else    
                {
                    HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.BadRequest);
                    return new BadRequestObjectResult("Please pass a valid source url in the request body.");
                }
            }
            else    
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.BadRequest);
                return new BadRequestObjectResult("Please pass a valid request body.");
            }
        }
    }
}
