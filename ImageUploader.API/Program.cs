using Amazon.S3;
using Amazon.S3.Model;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAWSService<IAmazonS3>();
var app = builder.Build();

var bucketName = "cwm-image-input"; // Replace with your actual bucket name

app.MapPost("/upload", async (HttpRequest request, IAmazonS3 s3Client) =>
{
    var form = await request.ReadFormAsync();
    var files = form.Files;

    if (files == null || files.Count == 0)
        return Results.BadRequest("No files uploaded.");

    var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
    var uploadTasks = new List<Task>();
    var uploadedKeys = new List<string>();

    foreach (var file in files)
    {
        if (!allowedTypes.Contains(file.ContentType))
            continue;

        var key = $"images/{Guid.NewGuid()}_{file.FileName}";
        uploadedKeys.Add(key);

        var stream = file.OpenReadStream();

        var s3Request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = stream,
            ContentType = file.ContentType
        };

        // Start the upload task
        uploadTasks.Add(s3Client.PutObjectAsync(s3Request));
    }

    await Task.WhenAll(uploadTasks);

    return Results.Ok(new { UploadedFiles = uploadedKeys });
});

app.UseHttpsRedirection();
app.Run();
