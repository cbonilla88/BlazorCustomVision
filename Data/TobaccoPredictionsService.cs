using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BlazorCustomVision.Data
{
    public class TobaccoPredictionsService
    {
        private const string predictionKey = "<Your prediction key here>";
        private const string projectId = "<Your project id here>";
        private const string publishedModelName = "<Your iteration model name here>";
        private readonly string predictionFromURL = "<Your prediction url here>";
        private const double minimumThreshold = 0.3D;

        public async Task<CustomVisionResponse> PredictFromURL(string imageFileUrlPath)
        {
            if (string.IsNullOrWhiteSpace(imageFileUrlPath)) return null;

            try
            {
                var stopWatch = new Stopwatch();
                HttpResponseMessage response;
                var customVisionResponse = new CustomVisionResponse();
                byte[] byteData = null;

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Prediction-Key", predictionKey);

                    var url = predictionFromURL;
                    var request = new CustomVisionRequest { Url = imageFileUrlPath };
                    var json = JsonConvert.SerializeObject(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    stopWatch.Start();
                    response = await client.PostAsync(url, content).ConfigureAwait(false);
                    stopWatch.Stop();
                }

                string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    customVisionResponse.Error = $"Error {response.StatusCode}: {responseContent}";
                    return customVisionResponse;
                }

                var tempCustomVisionResponse = JsonConvert.DeserializeObject<CustomVisionResponse>(responseContent);
                customVisionResponse.Id = tempCustomVisionResponse.Id;
                customVisionResponse.Created = tempCustomVisionResponse.Created;
                customVisionResponse.Iteration = tempCustomVisionResponse.Iteration;
                customVisionResponse.Project = tempCustomVisionResponse.Project;
                customVisionResponse.Predictions = new List<Prediction>();

                foreach (var prediction in tempCustomVisionResponse.Predictions.Where(x => x.Probability >= minimumThreshold))
                {
                    var objPrediction = new Prediction();
                    objPrediction.TagId = prediction.TagId;
                    objPrediction.TagName = prediction.TagName;
                    objPrediction.Probability = prediction.Probability;

                    objPrediction.BoundingBox = new BoundingBox
                    {
                        Height = prediction.BoundingBox.Height,
                        Width = prediction.BoundingBox.Width,
                        Left = prediction.BoundingBox.Left,
                        Top = prediction.BoundingBox.Top
                    };

                    customVisionResponse.Predictions.Add(objPrediction);
                }

                byteData = await GetImageAsByteArrayAsync(imageFileUrlPath).ConfigureAwait(false);
                customVisionResponse = AddCustomVisionResponseToImage(customVisionResponse, byteData);

                var ts = stopWatch.Elapsed;
                string elapsedTime = string.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds);
                customVisionResponse.ElapsedTime = elapsedTime;

                return customVisionResponse;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        private CustomVisionResponse AddCustomVisionResponseToImage(CustomVisionResponse customVisionResponse, byte[] content)
        {
            var redPen = new Pen(Color.Red, 3);
            var font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Regular);

            using (var ms = new MemoryStream(content))
            {
                using (var img = new Bitmap(ms))
                {
                    using (var graphics = Graphics.FromImage(img))
                    {
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                        foreach (var predict in customVisionResponse.Predictions)
                        {
                            var x = predict.BoundingBox.Left * img.Width;
                            var y = predict.BoundingBox.Top * img.Height;
                            var width = predict.BoundingBox.Width * img.Width;
                            var height = predict.BoundingBox.Height * img.Height;

                            var rect = new Rectangle(
                                (int)(x),
                                (int)(y),
                                (int)(width),
                                (int)(height));

                            // Prediction box
                            graphics.DrawRectangle(redPen, rect);

                            // Set up string.
                            string measureString = $"{predict.TagName}: {predict.Probability:P1}";

                            // Set maximum layout size.
                            SizeF layoutSize = new SizeF((float)width, (float)height);

                            // Measure string.
                            SizeF stringSize = new SizeF();
                            stringSize = graphics.MeasureString(measureString, font, layoutSize);

                            // Draw rectangle representing size of string.
                            graphics.FillRectangle(Brushes.Red, (int)x, (int)(y - stringSize.Width * 0.2), stringSize.Width, stringSize.Height);

                            // Draw string to screen.
                            graphics.DrawString(measureString, font, Brushes.White, new PointF((float)x, (float)(y - stringSize.Width * 0.15)));
                        }

                        using (var newMS = new MemoryStream())
                        {
                            img.Save(newMS, img.RawFormat);
                            byte[] imageBytes = newMS.ToArray();
                            var base64String = Convert.ToBase64String(imageBytes);
                            customVisionResponse.ImageInBase64 = base64String;
                        }

                        return customVisionResponse;
                    }
                }
            }
        }

        private async Task<byte[]> GetImageAsByteArrayAsync(string imageFileUrlPath)
        {
            using (var webClient = new WebClient())
            {
                var imageBytes = await webClient.DownloadDataTaskAsync(new Uri(imageFileUrlPath)).ConfigureAwait(false);
                return imageBytes;
            }
        }
    }
}
