﻿// Copyright (c) Mathias Thierbach
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PbiTools.Serialization
{
    using FileSystem;

    public static class JsonExtensions
    {
        /// <summary>
        /// Removes the specified property from its parent, parses its content as a string-encoded <see cref="JObject"/>, and saves the parsed json object
        /// to the provided <see cref="IProjectFolder"/>, using the property name as the filename (unless a null folder is provided).
        /// </summary>
        /// <returns>The <see cref="JObject"/> extracted from the property.</returns>
        public static JObject ExtractObject(this JObject parent, string property, IProjectFolder folder)
        {
            return parent.ExtractToken<JObject>(property, folder);
        }

        /// <summary>
        /// Removes the specified property from its parent, parses its content as a string-encoded <see cref="JArray"/>, and saves the parsed json array
        /// to the provided <see cref="IProjectFolder"/>, using the property name as the filename (unless a null folder is provided).
        /// </summary>
        /// <returns>The <see cref="JArray"/> extracted from the property.</returns>
        public static JArray ExtractArray(this JObject parent, string property, IProjectFolder folder)
        {
            return parent.ExtractToken<JArray>(property, folder);
        }

        public static IEnumerable<T> ExtractArrayAs<T>(this JObject parent, string property)
        {
            var array = parent.ExtractToken<JArray>(property, folder: null);
            if (array != null) return array.OfType<T>();
            else return new T[0];
        }

        /// <summary>
        /// Removes the named property containing an array from the Json object, and returns the array elements as an <see cref="IEnumerable{T}"/>.
        /// Returns an empty enumeration if the property does not exist, but it will fail if the property does not contain an array.
        /// Any array elements that are not of type <see cref="T"/> will be skipped.
        /// </summary>
        public static IEnumerable<T> RemoveArrayAs<T>(this JObject parent, string property)
        {
            var array = parent[property]?.Value<JArray>();
            parent.Remove(property);

            return array != null ? array.OfType<T>() : new T[0];
        }

        /// <summary>
        /// Parses a string-encoded json token out of a json object property, removes the property,
        /// and optionally saves the token in the <see cref="IProjectFolder"/>.
        /// </summary>
        public static T ExtractToken<T>(this JObject parent, string property, IProjectFolder folder = null) where T : JToken
        {
            T obj = null;
            var token = parent[property];
            if (token != null)
            {
                parent.Remove(property);
                obj = JToken.Parse(token.Value<string>()) as T;
                obj.Save(property, folder);
            }
            return obj;
        }


        public static JObject InsertToken<T>(this JObject parent, string property, T token) where T : JToken
        {
            if (token != null)
                parent[property] = token.ToString(formatting: Newtonsoft.Json.Formatting.None);
            return parent;
        }

        public static JObject InsertObjectFromFile(this JObject parent, IProjectFolder folder, string fileName)
        { 
            var objectFile = folder.GetFile(fileName);
            if (objectFile.Exists()) {
                parent.InsertToken(Path.GetFileNameWithoutExtension(fileName), objectFile.ReadJson());
            }
            return parent;
        }

        public static JObject InsertArrayFromFile(this JObject parent, IProjectFolder folder, string fileName)
        { 
            var arrayFile = folder.GetFile(fileName);
            if (arrayFile.Exists()) {
                parent.InsertToken(Path.GetFileNameWithoutExtension(fileName), arrayFile.ReadJsonArray());
            }
            return parent;
        }

        /// <summary>
        /// Saves the <see cref="JToken"/> to the <see cref="IProjectFolder"/> using the <see cref="name"/> provided,
        /// applying all transforms (if any) in specified order.
        /// </summary>
        public static void Save(this JToken token, string name, IProjectFolder folder, params Func<JToken, JToken>[] transforms)
        {
            if (folder == null || token == null) return;
            if (transforms != null) token = transforms.Aggregate(token, (t, func) => func(t));
            folder.Write(token, $"{name}.json");
        }

        public static T ReadPropertySafe<T>(this JObject json, string property, T defaultValue = default(T))
        { 
            if (json == null) throw new ArgumentNullException("json");
            if (json.TryGetValue(property, StringComparison.InvariantCultureIgnoreCase, out var value))
            {
                try
                { 
                    return value.ToObject<T>();
                }
                catch (Exception e)
                {
                    Serilog.Log.Error(e, "Json conversion error occurred reading Property {PropertyName} as Type {TypeName}", property, typeof(T).Name);
                }
            }
            return defaultValue;
        }
    }
}