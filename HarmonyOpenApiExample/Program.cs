﻿using HarmonyLib;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HarmonyOpenApiExample
{
    class Program
    {
        static void Main(string[] args)
        {
            var operation = new OpenApiOperation()
            {
                RequestBody = new OpenApiRequestBody
                {
                    Content = new Dictionary<string, OpenApiMediaType>()
                    {
                        ["test"] = new OpenApiMediaType()
                    },
                    Required = true,
                    Extensions = new Dictionary<string, IOpenApiExtension>()
                    {
                        ["name"] = new OpenApiString("subscriber")
                    }
                }
            };

            // Use Harmony
            // UseHarmony(operation);

            // Use DispatchProxy
            // UseDispatchProxy(operation);

            // Use Custom Class
            UseCustomClass(operation);
        }

        private static void UseCustomClass(OpenApiOperation operation)
        {
            OpenApiOperation operation2 = new OpenApiOperation2(operation);

            using (var writer = new StringWriter())
            {
                operation2.SerializeAsV2(new OpenApiJsonWriter(writer));
                Console.Write(writer.ToString());
            }
        }

        private static void UseDispatchProxy(OpenApiOperation operation)
        {
            var decorated = OpenApiOperationProxy.Create(operation);
            using (var writer = new StringWriter())
            {
                decorated.SerializeAsV2(new OpenApiJsonWriter(writer));
                Console.Write(writer.ToString());
            }
        }

        private static void UseHarmony(OpenApiOperation operation)
        {
            var harmony = new Harmony("com.test.patch");
            var mOriginal = typeof(OpenApiOperation).GetMethod("SerializeAsV2");
            var mPrefix = AccessTools.Method(typeof(Program), "Prefix");
            var mTranspiler = AccessTools.Method(typeof(Program), "Transpiler");
            harmony.Patch(mOriginal, new HarmonyMethod(mPrefix), transpiler: new HarmonyMethod(mTranspiler));

            using (var writer2 = new StringWriter())
            {
                operation.SerializeAsV2(new OpenApiJsonWriter(writer2));
                Console.Write(writer2.ToString());
            }
        }

        public static bool Prefix(OpenApiOperation __instance, ref IOpenApiWriter writer)
        {
            Console.WriteLine("Invoke Prefix");
            var Tags = __instance.Tags;
            var Summary = __instance.Summary;
            var Description = __instance.Description;
            var ExternalDocs = __instance.ExternalDocs;
            var OperationId = __instance.OperationId;
            var Parameters = __instance.Parameters;
            var RequestBody = __instance.RequestBody;
            var Responses = __instance.Responses;
            var Extensions = __instance.Extensions;
            var Servers = __instance.Servers;
            var Deprecated = __instance.Deprecated;
            var Security = __instance.Security;


            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.WriteStartObject();

            // tags
            writer.WriteOptionalCollection(
                OpenApiConstants.Tags,
                Tags,
                (w, t) =>
                {
                    t.SerializeAsV2(w);
                });

            // summary
            writer.WriteProperty(OpenApiConstants.Summary, Summary);

            // description
            writer.WriteProperty(OpenApiConstants.Description, Description);

            // externalDocs
            writer.WriteOptionalObject(OpenApiConstants.ExternalDocs, ExternalDocs, (w, e) => e.SerializeAsV2(w));

            // operationId
            writer.WriteProperty(OpenApiConstants.OperationId, OperationId);

            IList<OpenApiParameter> parameters;
            if (Parameters == null)
            {
                parameters = new List<OpenApiParameter>();
            }
            else
            {
                parameters = new List<OpenApiParameter>(Parameters);
            }

            if (RequestBody != null)
            {
                // consumes
                writer.WritePropertyName(OpenApiConstants.Consumes);
                writer.WriteStartArray();
                var consumes = RequestBody.Content.Keys.Distinct().ToList();
                foreach (var mediaType in consumes)
                {
                    writer.WriteValue(mediaType);
                }

                writer.WriteEndArray();

                // This is form data. We need to split the request body into multiple parameters.
                if (consumes.Contains("application/x-www-form-urlencoded") ||
                    consumes.Contains("multipart/form-data"))
                {
                    foreach (var property in RequestBody.Content.First().Value.Schema.Properties)
                    {
                        parameters.Add(
                            new OpenApiParameter
                            {
                                Description = property.Value.Description,
                                Name = property.Key,
                                Schema = property.Value,
                                Required = RequestBody.Content.First().Value.Schema.Required.Contains(property.Key)
                            });
                    }
                }
                else
                {
                    var content = RequestBody.Content.Values.FirstOrDefault();
                    var customName = Extensions["name"];
                    var bodyParameter = new OpenApiParameter
                    {
                        Description = RequestBody.Description,
                        Name = customName != null ? (customName as OpenApiString).Value : "body",
                        Schema = content?.Schema ?? new OpenApiSchema(),
                        Required = RequestBody.Required
                    };

                    parameters.Add(bodyParameter);
                }
            }

            if (Responses != null)
            {
                var produces = Responses.Where(r => r.Value.Content != null)
                    .SelectMany(r => r.Value.Content?.Keys)
                    .Distinct()
                    .ToList();

                if (produces.Any())
                {
                    // produces
                    writer.WritePropertyName(OpenApiConstants.Produces);
                    writer.WriteStartArray();
                    foreach (var mediaType in produces)
                    {
                        writer.WriteValue(mediaType);
                    }

                    writer.WriteEndArray();
                }
            }

            // parameters
            // Use the parameters created locally to include request body if exists.
            writer.WriteOptionalCollection(OpenApiConstants.Parameters, parameters, (w, p) => p.SerializeAsV2(w));

            // responses
            writer.WriteRequiredObject(OpenApiConstants.Responses, Responses, (w, r) => r.SerializeAsV2(w));

            // schemes
            // All schemes in the Servers are extracted, regardless of whether the host matches
            // the host defined in the outermost Swagger object. This is due to the 
            // inaccessibility of information for that host in the context of an inner object like this Operation.
            if (Servers != null)
            {
                var schemes = Servers.Select(
                        s =>
                        {
                            Uri.TryCreate(s.Url, UriKind.RelativeOrAbsolute, out var url);
                            return url?.Scheme;
                        })
                    .Where(s => s != null)
                    .Distinct()
                    .ToList();

                writer.WriteOptionalCollection(OpenApiConstants.Schemes, schemes, (w, s) => w.WriteValue(s));
            }

            // deprecated
            writer.WriteProperty(OpenApiConstants.Deprecated, Deprecated, false);

            // security
            writer.WriteOptionalCollection(OpenApiConstants.Security, Security, (w, s) => s.SerializeAsV2(w));

            // specification extensions
            writer.WriteExtensions(Extensions, OpenApiSpecVersion.OpenApi2_0);

            writer.WriteEndObject();

            // Let's do nothing, and not call the original method.
            return false;
        }

        public static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
        {
            Console.WriteLine("Invoke Transpiler");
            return instructions;
        }
    }
}
