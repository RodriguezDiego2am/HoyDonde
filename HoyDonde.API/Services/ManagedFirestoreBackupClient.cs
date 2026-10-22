using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using HoyDonde.API.Models;
using Microsoft.Extensions.Configuration;

namespace HoyDonde.API.Services
{
    /// <summary>
    /// Thin REST adapter for the managed Firestore Admin and Cloud Storage APIs. It starts native
    /// Firestore export/import operations; application documents are never read or serialized here.
    /// </summary>
    public class ManagedFirestoreBackupClient : IManagedFirestoreBackupClient
    {
        private const string FirestoreScope = "https://www.googleapis.com/auth/datastore";
        private const string StorageScope = "https://www.googleapis.com/auth/devstorage.read_write";
        private const string ApiBaseUrl = "https://firestore.googleapis.com/v1/";
        private const string StorageApiBaseUrl = "https://storage.googleapis.com/storage/v1/";
        private readonly HttpClient _httpClient;
        private readonly string _projectId;
        private readonly string _credentialsPath;
        private GoogleCredential? _credential;

        public ManagedFirestoreBackupClient(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _projectId = configuration["Firebase:ProjectId"]
                ?? throw new InvalidOperationException("Firebase:ProjectId es obligatorio para las copias de Firestore.");
            _credentialsPath = configuration["Firebase:CredentialsPath"] ?? "firebase-service-account.json";
        }

        public Task<ManagedFirestoreOperation> StartExportAsync(string outputUriPrefix) =>
            StartAsync($"projects/{_projectId}/databases/(default):exportDocuments", new { outputUriPrefix });

        public Task<ManagedFirestoreOperation> StartImportAsync(string inputUriPrefix) =>
            StartAsync($"projects/{_projectId}/databases/(default):importDocuments", new { inputUriPrefix });

        public async Task<ManagedFirestoreOperation> GetOperationAsync(string operationName, FirestoreBackup.BackupOperation operation)
        {
            if (string.IsNullOrWhiteSpace(operationName)) throw new ArgumentException("La operación administrada es obligatoria.");

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBaseUrl + operationName, UriKind.Absolute));
            await AuthorizeAsync(request);
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return ParseOperation(await response.Content.ReadAsStringAsync());
        }

        public async Task DeleteExportObjectsAsync(string storageUri)
        {
            var (bucket, prefix) = ParseStorageUri(storageUri);
            var objectNames = new List<string>();
            string? pageToken = null;

            do
            {
                var listUri = $"{StorageApiBaseUrl}b/{Uri.EscapeDataString(bucket)}/o?prefix={Uri.EscapeDataString(prefix)}";
                if (!string.IsNullOrEmpty(pageToken)) listUri += $"&pageToken={Uri.EscapeDataString(pageToken)}";

                using var listRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(listUri, UriKind.Absolute));
                await AuthorizeAsync(listRequest);
                using var listResponse = await _httpClient.SendAsync(listRequest);
                listResponse.EnsureSuccessStatusCode();
                var page = ParseObjectPage(await listResponse.Content.ReadAsStringAsync());
                objectNames.AddRange(page.ObjectNames);
                pageToken = page.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            foreach (var objectName in objectNames)
            {
                var objectUri = $"{StorageApiBaseUrl}b/{Uri.EscapeDataString(bucket)}/o/{Uri.EscapeDataString(objectName)}";
                using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri(objectUri, UriKind.Absolute));
                await AuthorizeAsync(deleteRequest);
                using var deleteResponse = await _httpClient.SendAsync(deleteRequest);
                deleteResponse.EnsureSuccessStatusCode();
            }
        }

        private async Task<ManagedFirestoreOperation> StartAsync(string relativeUri, object body)
        {
            var json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ApiBaseUrl + relativeUri, UriKind.Absolute))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            await AuthorizeAsync(request);
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return ParseOperation(await response.Content.ReadAsStringAsync());
        }

        private async Task AuthorizeAsync(HttpRequestMessage request)
        {
            if (_credential == null)
            {
                GoogleCredential credential;
                if (File.Exists(_credentialsPath))
                {
                    credential = GoogleCredential.FromFile(_credentialsPath);
                }
                else if (Environment.GetEnvironmentVariable("K_SERVICE") != null)
                {
                    credential = GoogleCredential.GetApplicationDefault();
                }
                else
                {
                    throw new InvalidOperationException("No hay una credencial de Firebase configurada para las copias administradas.");
                }

                _credential = credential.CreateScoped(FirestoreScope, StorageScope);
            }

            var tokenAccess = _credential as ITokenAccess
                ?? throw new InvalidOperationException("La credencial de Firebase no puede generar tokens de acceso.");
            var token = await tokenAccess.GetAccessTokenForRequestAsync(null, default);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        private static (string Bucket, string Prefix) ParseStorageUri(string storageUri)
        {
            if (!Uri.TryCreate(storageUri, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "gs", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException("La ubicación de Storage de la copia no es válida.");
            }

            return (uri.Host, Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')));
        }

        private static (IReadOnlyList<string> ObjectNames, string? NextPageToken) ParseObjectPage(string body)
        {
            using var document = JsonDocument.Parse(body);
            var objectNames = new List<string>();
            if (document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString()))
                    {
                        objectNames.Add(name.GetString()!);
                    }
                }
            }

            var nextPageToken = document.RootElement.TryGetProperty("nextPageToken", out var pageToken)
                ? pageToken.GetString()
                : null;
            return (objectNames, nextPageToken);
        }

        private static ManagedFirestoreOperation ParseOperation(string body)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var name) || string.IsNullOrWhiteSpace(name.GetString()))
            {
                throw new InvalidOperationException("La API administrada no devolvió una operación válida.");
            }

            var completed = root.TryGetProperty("done", out var done) && done.GetBoolean();
            return new ManagedFirestoreOperation
            {
                Name = name.GetString()!,
                IsCompleted = completed,
                IsFailed = completed && root.TryGetProperty("error", out _),
            };
        }
    }
}
