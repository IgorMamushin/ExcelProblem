using System.Diagnostics.CodeAnalysis;
using Google.Api;
using Google.Protobuf.Reflection;
using Microsoft.AspNetCore.Routing.Patterns;

namespace ExcelDownloadProblem;

internal static class PlatformServiceDescriptorHelpers
{
    private static bool TryResolveDescriptors(MessageDescriptor messageDescriptor, string variable,
        [NotNullWhen(true)] out List<FieldDescriptor>? fieldDescriptors)
    {
        fieldDescriptors = null;
        var path = variable.AsSpan();
        var currentDescriptor = messageDescriptor;

        while (path.Length > 0)
        {
            var separator = path.IndexOf('.');

            string fieldName;
            if (separator != -1)
            {
                fieldName = path[..separator].ToString();
                path = path[(separator + 1)..];
            }
            else
            {
                fieldName = path.ToString();
                path = ReadOnlySpan<char>.Empty;
            }

            var field = currentDescriptor?.FindFieldByName(fieldName);
            if (field == null)
            {
                fieldDescriptors = null;
                return false;
            }

            fieldDescriptors ??= new List<FieldDescriptor>();

            fieldDescriptors.Add(field);

            currentDescriptor = field.FieldType == FieldType.Message ? field.MessageType : null;
        }

        return fieldDescriptors is not null;
    }

    public static bool TryResolvePattern(HttpRule http, [NotNullWhen(true)] out string? pattern,
        [NotNullWhen(true)] out string? verb)
    {
        switch (http.PatternCase)
        {
            case HttpRule.PatternOneofCase.Get:
                pattern = http.Get;
                verb = "GET";
                return true;
            case HttpRule.PatternOneofCase.Put:
                pattern = http.Put;
                verb = "PUT";
                return true;
            case HttpRule.PatternOneofCase.Post:
                pattern = http.Post;
                verb = "POST";
                return true;
            case HttpRule.PatternOneofCase.Delete:
                pattern = http.Delete;
                verb = "DELETE";
                return true;
            case HttpRule.PatternOneofCase.Patch:
                pattern = http.Patch;
                verb = "PATCH";
                return true;
            case HttpRule.PatternOneofCase.Custom:
                pattern = http.Custom.Path;
                verb = http.Custom.Kind;
                return true;
            case HttpRule.PatternOneofCase.None:
            default:
                pattern = null;
                verb = null;
                return false;
        }
    }

    public static Dictionary<string, List<FieldDescriptor>> ResolveRouteParameterDescriptors(RoutePattern pattern,
        MessageDescriptor messageDescriptor)
    {
        var routeParameterDescriptors = new Dictionary<string, List<FieldDescriptor>>(StringComparer.Ordinal);
        foreach (var routeParameter in pattern.Parameters)
        {
            if (!TryResolveDescriptors(messageDescriptor, routeParameter.Name, out var fieldDescriptors))
            {
                throw new InvalidOperationException(
                    $"Couldn't find matching field for route parameter '{routeParameter.Name}' on {messageDescriptor.Name}.");
            }

            routeParameterDescriptors.Add(routeParameter.Name, fieldDescriptors);
        }

        return routeParameterDescriptors;
    }

    public static void ResolveBodyDescriptor(string body, MethodDescriptor methodDescriptor,
        out MessageDescriptor? bodyDescriptor, out List<FieldDescriptor>? bodyFieldDescriptors,
        out bool bodyDescriptorRepeated)
    {
        bodyDescriptor = null;
        bodyFieldDescriptors = null;
        bodyDescriptorRepeated = false;

        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        if (!string.Equals(body, "*", StringComparison.Ordinal))
        {
            if (!TryResolveDescriptors(methodDescriptor.InputType, body, out bodyFieldDescriptors))
            {
                throw new InvalidOperationException(
                    $"Couldn't find matching field for body '{body}' on {methodDescriptor.InputType.Name}.");
            }

            var leafDescriptor = bodyFieldDescriptors.Last();
            if (leafDescriptor.IsRepeated)
            {
                // A repeating field isn't a message type. The JSON parser will parse using the containing
                // type to get the repeating collection.
                bodyDescriptor = leafDescriptor.ContainingType;
                bodyDescriptorRepeated = true;
            }
            else
            {
                bodyDescriptor = leafDescriptor.MessageType;
            }
        }
        else
        {
            bodyDescriptor = methodDescriptor.InputType;
        }
    }

    // HttpRule: https://cloud.google.com/endpoints/docs/grpc-service-config/reference/rpc/google.api#google.api.HttpRule
    public static List<(string Name, FieldDescriptor Field)> ResolveQueryParameterDescriptors(
        Dictionary<string, List<FieldDescriptor>> routeParameters,
        MethodDescriptor methodDescriptor,
        MessageDescriptor? bodyDescriptor,
        List<FieldDescriptor>? bodyFieldDescriptors
    )
    {
        var queryDescriptors = new List<(string, FieldDescriptor)>();

        if (methodDescriptor.InputType.Fields.InDeclarationOrder().Count <= routeParameters.Count)
        {
            return queryDescriptors;
        }

        var allParameters = methodDescriptor.InputType.Fields.InDeclarationOrder().ToList();

        var allParametersName = methodDescriptor.InputType.Fields
            .InDeclarationOrder()
            .Select(x => x.Name)
            .ToList();

        foreach (var pathParameter in routeParameters)
        {
            allParametersName.Remove(pathParameter.Key);
        }

        if (bodyDescriptor is not null)
        {
            if (bodyFieldDescriptors is not null)
            {
                // body with field name
                foreach (var bodyFieldDescriptor in bodyFieldDescriptors)
                {
                    allParametersName.Remove(bodyFieldDescriptor.Name);
                }
            }
            else
            {
                // body with wildcard
                return new List<(string, FieldDescriptor)>();
            }
        }

        // handle existing method parameters
        foreach (var parameterName in allParametersName)
        {
            var field = allParameters
                .Where(x => x.Name == parameterName)
                .Select(x => x)
                .First();

            allParameters.Remove(field);

            var descriptors = GetRecursiveQueryDescriptors(parameterName, field, new List<(string name, FieldDescriptor field)>());

            descriptors.ForEach(descriptor => queryDescriptors.Add(descriptor));
        }

        return queryDescriptors;
    }

    private static List<(string name, FieldDescriptor field)> GetRecursiveQueryDescriptors(
        string parameterName,
        FieldDescriptor field,
        List<(string name, FieldDescriptor field)> queryDescriptors)
    {
        var queryDescriptor = (Name: parameterName, Field: field);

        if (field.FieldType is not (FieldType.Message or FieldType.Group))
        {
            queryDescriptors.Add(queryDescriptor);

            return queryDescriptors;
        }

        var innerDescriptors = field.MessageType.Fields.InDeclarationOrder();

        var jsonNames = innerDescriptors.Select(descriptor => descriptor.Name).ToList();

        for (var index = 0; index < innerDescriptors.Count; index++)
        {
            GetRecursiveQueryDescriptors(parameterName + "." + jsonNames[index], innerDescriptors[index], queryDescriptors);
        }

        return queryDescriptors;
    }
}