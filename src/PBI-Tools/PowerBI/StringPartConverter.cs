﻿// Copyright (c) Mathias Thierbach
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using System.Text;

namespace PbiTools.PowerBI
{
    public class StringPartConverter : IPowerBIPartConverter<string>
    {
        private readonly Encoding _encoding;

        public StringPartConverter(Uri partUri) : this(partUri, Encoding.Unicode)
        {
        }

        public StringPartConverter(Uri partUri, Encoding encoding)
        {
            this.PartUri = partUri;
            _encoding = encoding;
        }

        public Uri PartUri { get; }

        public bool IsOptional { get; set; } = true;
        public string ContentType { get; set; } = PowerBIPartConverters.ContentTypes.DEFAULT;

        public string FromPackagePart(Func<Stream> part, string contentType)
        {
            if (!part.TryGetStream(out var stream)) return default(string);
            using (var reader = new StreamReader(stream, _encoding))
            {
                return reader.ReadToEnd();
            }
        }

        public Func<Stream> ToPackagePart(string content)
        {
            if (content == null) return null;
            return () => new MemoryStream(_encoding.GetBytes(content));
        }
    }
}