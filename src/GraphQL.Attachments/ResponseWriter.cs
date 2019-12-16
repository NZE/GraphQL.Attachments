﻿using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GraphQL.Attachments
{
    /// <summary>
    /// Handles writing a <see cref="AttachmentExecutionResult"/> to a <see cref="HttpResponse"/>.
    /// </summary>
    public static class ResponseWriter
    {
        /// <summary>
        /// Writes <paramref name="result"/> to <paramref name="response"/>.
        /// </summary>
        public static Task WriteResult(HttpResponse response, AttachmentExecutionResult result)
        {
            Guard.AgainstNull(nameof(response), response);
            Guard.AgainstNull(nameof(result), result);
            var executionResult = result.ExecutionResult;
            var attachments = (OutgoingAttachments) result.Attachments;
            if (executionResult.Errors?.Count > 0)
            {
                response.StatusCode = (int) HttpStatusCode.BadRequest;
                return WriteStream(executionResult, response);
            }

            if (!attachments.HasPendingAttachments)
            {
                return WriteStream(executionResult, response);
            }

            return WriteMultipart(response, executionResult, attachments);
        }

        static async Task WriteMultipart(HttpResponse response, ExecutionResult result, OutgoingAttachments attachments)
        {
            var httpContents = new List<HttpContent>();
            try
            {
                using var multipart = new MultipartFormDataContent();
                var serializedResult = JsonConvert.SerializeObject(result);
                multipart.Add(new StringContent(serializedResult));

                foreach (var attachment in attachments.Inner)
                {
                    httpContents.Add(await AddAttachment(attachment, multipart));
                }

                response.ContentLength = multipart.Headers.ContentLength;
                response.ContentType = multipart.Headers.ContentType.ToString();
                await multipart.CopyToAsync(response.Body);
            }
            finally
            {
                foreach (var httpContent in httpContents)
                {
                    httpContent.Dispose();
                }

                foreach (var cleanup in attachments.Inner.Select(x => x.Value.Cleanup))
                {
                    cleanup?.Invoke();
                }
            }
        }

        static async Task<HttpContent> AddAttachment(KeyValuePair<string, Outgoing> attachment, MultipartFormDataContent multipart)
        {
            var outgoing = attachment.Value;
            var httpContent = await outgoing.ContentBuilder();
            if (outgoing.Headers != null)
            {
                foreach (var (key, value) in outgoing.Headers)
                {
                    httpContent.Headers.Add(key, value);
                }
            }

            multipart.Add(httpContent, attachment.Key, attachment.Key);
            return httpContent;
        }

        static async Task WriteStream(ExecutionResult result, HttpResponse response)
        {
            response.Headers.Add("Content-Type", "application/json");
            await using var streamWriter = new StreamWriter(response.Body, Encoding.UTF8, 1024, true);
            await streamWriter.WriteAsync(JsonConvert.SerializeObject(result));
        }
    }
}