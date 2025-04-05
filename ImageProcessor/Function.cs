using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ImageProcessor;

public class Function
{
    private readonly IAmazonS3 _s3Client;
    private const string OutputBucket = "cwm-image-output";

    public Function() => _s3Client = new AmazonS3Client();

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        // Pretty-print the full SQS event
        var fullEventJson = JsonSerializer.Serialize(
            sqsEvent,
            new JsonSerializerOptions { WriteIndented = true }
        );

        context.Logger.LogInformation("Full SQS Event:");
        context.Logger.LogInformation(fullEventJson);

        foreach (var sqsRecord in sqsEvent.Records)
        {
            try
            {
                // Log raw SQS body
                context.Logger.LogInformation("Raw SQS Message Body:");
                context.Logger.LogInformation(sqsRecord.Body);

                var s3Event = JsonSerializer.Deserialize<S3Event>(sqsRecord.Body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (s3Event == null) continue;

                // Log parsed S3 event structure
                context.Logger.LogInformation("Parsed S3 Event JSON:");
                var s3EventJson = JsonSerializer.Serialize(s3Event, new JsonSerializerOptions { WriteIndented = true });
                context.Logger.LogInformation(s3EventJson);

                foreach (var record in s3Event.Records)
                {
                    var inputBucket = record.S3.Bucket.Name;
                    var objectKey = Uri.UnescapeDataString(record.S3.Object.Key.Replace('+', ' '));

                    context.Logger.LogInformation($"Processing: {inputBucket}/{objectKey}");

                    using var originalImageStream = await _s3Client.GetObjectStreamAsync(inputBucket, objectKey, null);

                    using var image = await Image.LoadAsync(originalImageStream);
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(300, 0)
                    }));

                    using var outputStream = new MemoryStream();
                    await image.SaveAsJpegAsync(outputStream);
                    outputStream.Position = 0;

                    var outputKey = $"thumbnails/{Path.GetFileName(objectKey)}";

                    var putRequest = new PutObjectRequest
                    {
                        BucketName = OutputBucket,
                        Key = outputKey,
                        InputStream = outputStream,
                        ContentType = "image/jpeg"
                    };

                    await _s3Client.PutObjectAsync(putRequest);
                    context.Logger.LogInformation($"Successfully processed: {objectKey}");
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error processing record: {ex.Message}");
                throw;
            }
        }
    }
}