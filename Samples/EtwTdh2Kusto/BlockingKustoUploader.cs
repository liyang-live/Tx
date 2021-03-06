﻿using Kusto.Cloud.Platform.Data;
using Kusto.Data;
using Kusto.Ingest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Etw2Kusto
{
    class BlockingKustoUploader<T> : IObserver<T>
    {
        public string TableName { get; private set; }
        public int BatchSize { get; private set; }

        private KustoConnectionStringBuilder csb { get; set; }

        IKustoIngestClient _ingestClient;
        KustoIngestionProperties _ingestionProperties;
        string[] _fields;
        List<T> _nextBatch;
        List<T> _currentBatch;
        DateTime _lastUploadTime;
        TimeSpan _flushDuration;
        public AutoResetEvent Completed { get; private set; }

        public BlockingKustoUploader(
            KustoConnectionStringBuilder kscb,
            string tableName,
            int batchSize,
            TimeSpan flushDuration)
        {
            csb = kscb;
            TableName = TableName;
            BatchSize = batchSize;
            _flushDuration = flushDuration;
            _lastUploadTime = DateTime.Now;
            Completed = new AutoResetEvent(false);

            _ingestionProperties = new KustoIngestionProperties(csb.InitialCatalog, tableName);
            _fields = typeof(EtwEvent).GetFields().Select(f => f.Name).ToArray();

            if (csb.DataSource.StartsWith("https://ingest-"))
            {
                _ingestClient = KustoIngestFactory.CreateQueuedIngestClient(csb);
            }
            else
            {
                _ingestClient = KustoIngestFactory.CreateDirectIngestClient(csb);
            }

            _nextBatch = new List<T>();
        }

        public void UploadBatch()
        {
            if (_currentBatch != null)
            {
                throw new Exception("Upload must not be called before the batch currently being uploaded is complete");
            }

            _currentBatch = _nextBatch;
            _nextBatch = new List<T>();

            var data = new EnumerableDataReader<T>(_currentBatch, _fields);

            try
            {
                _ingestClient.IngestFromDataReader(data, _ingestionProperties);
                int recordsUploaded = _currentBatch.Count;
                _currentBatch = null;
                _lastUploadTime = DateTime.Now;

                Console.Write("{0} ", recordsUploaded);
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public void OnNext(T value)
        {
            DateTime now = DateTime.Now;
            if (_nextBatch.Count >= BatchSize
                || (_flushDuration != TimeSpan.MaxValue && now > _lastUploadTime + _flushDuration))
            {
                UploadBatch();
            }

            _nextBatch.Add(value);
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        public void OnCompleted()
        {
            UploadBatch();
            Console.WriteLine("Completed!");
            Completed.Set();
        }
    }
}