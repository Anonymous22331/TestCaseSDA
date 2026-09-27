using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Dapper;
using FluentValidation;
using Npgsql;
using TestCaseSDA.Models;

namespace TestCaseSDA.Services;

public sealed class PageProcessingService(
    IValidator<ProcessRequest> validator,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<PageProcessingService> logger)
{
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    public async Task<ProcessResponse> ProcessAsync(
        ProcessRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var validation = validator.Validate(request);
            if (!validation.IsValid)
            {
                var error = validation.Errors[0];
                return ProcessResponse.Error(error.ErrorCode, error.ErrorMessage);
            }

            if (!TryDecode(request.UrlB64!, out var urlBytes))
                return ProcessResponse.Error("INVALID_URL_BASE64", "url_b64 must contain valid Base64.");
            if (!TryDecode(request.PageB64!, out var pageBytes))
                return ProcessResponse.Error("INVALID_PAGE_BASE64", "page_b64 must contain valid Base64.");
            if (!TryDecode(request.KeyBytesB64!, out var key))
                return ProcessResponse.Error("INVALID_KEY_BASE64", "key_bytes_b64 must contain valid Base64.");
            if (!TryDecode(request.EncryptedTextBytesB64!, out var encrypted))
                return ProcessResponse.Error("INVALID_ENCRYPTED_TEXT_BASE64", "encrypted_text_bytes_b64 must contain valid Base64.");
            if (key.Length != 32)
                return ProcessResponse.Error("INVALID_AES_KEY", "AES-256 requires a 32-byte key.");
            if (encrypted.Length == 0 || encrypted.Length % 16 != 0)
                return ProcessResponse.Error("DECRYPTION_ERROR", "Ciphertext must contain complete 16-byte AES blocks.");

            var url = Encoding.UTF8.GetString(urlBytes);
            var html = Encoding.UTF8.GetString(pageBytes);
            string plainText;
            try
            {
                plainText = Decrypt(encrypted, key);
            }
            catch (CryptographicException)
            {
                return ProcessResponse.Error("DECRYPTION_ERROR", "Unable to decrypt the supplied ciphertext.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var context = BrowsingContext.New(Configuration.Default);
            using var document = await context.OpenAsync(
                response => response.Content(html), cancellationToken);
            IHtmlCollection<IElement> elements;
            try
            {
                elements = document.QuerySelectorAll(request.Selector!);
            }
            catch (DomException)
            {
                return ProcessResponse.Error("INVALID_SELECTOR", "selector must be a valid CSS selector.");
            }

            var rows = elements.Select(element => new
            {
                AttributeValue = element.GetAttribute(request.Attribute!),
                Html = element.OuterHtml
            }).ToArray();
            var emails = EmailRegex.Matches(html).Select(match => match.Value).ToList();
            cancellationToken.ThrowIfCancellationRequested();

            if (rows.Length > 0)
            {
                await using var connection = new NpgsqlConnection(
                    configuration.GetConnectionString("Postgres"));
                await connection.OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO elements (attribute_value, html) VALUES (@AttributeValue, @Html)",
                    rows, transaction, cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
            }

            return new ProcessResponse
            {
                Url = url,
                DecryptedPlainText = plainText,
                ElementsCount = rows.Length,
                ElementsAttrList = rows.Select(row => row.AttributeValue ?? "").ToList(),
                EmailsCount = emails.Count,
                EmailsList = emails
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            logger.LogError(exception, "Unable to save the selected elements.");
            return ProcessResponse.Error("DATABASE_ERROR", "Unable to save elements to PostgreSQL.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Page processing failed.");
            return ProcessResponse.Error("INTERNAL_ERROR", exception.Message);
        }
    }

    private static bool TryDecode(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string Decrypt(byte[] encrypted, byte[] key)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        using var decryptor = aes.CreateDecryptor();
        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length));
    }
}
