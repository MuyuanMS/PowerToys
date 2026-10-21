// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Timer = System.Timers.Timer;

#nullable enable

namespace SamplePagesExtension;

/// <summary>The form behind <see cref="SampleChartsPage"/>, with data that changes every second.</summary>
internal sealed partial class SampleChartsForm : FormContent, IDisposable
{
    private const int HistoryLength = 30;

    private readonly Random _random = new(42);
    private readonly double[] _requests = new double[HistoryLength];
    private readonly double[] _errors = new double[HistoryLength];
    private readonly object _timerLock = new();
    private Timer? _timer;
    private bool _disposed;
    private double _load = 42;

    public SampleChartsForm()
    {
        for (var i = 0; i < HistoryLength; i++)
        {
            Advance();
        }

        TemplateJson = SampleChartsTemplate.Json;
        DataJson = CreateData();
    }

    public void Start()
    {
        lock (_timerLock)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }

            _timer = new Timer(1000) { AutoReset = false };
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }
    }

    public void Dispose()
    {
        lock (_timerLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_timer is not null)
            {
                _timer.Stop();
                _timer.Elapsed -= OnTimerElapsed;
                _timer.Dispose();
                _timer = null;
            }
        }
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        Advance();
        var data = CreateData();
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        DataJson = data;

        lock (_timerLock)
        {
            if (!_disposed)
            {
                _timer?.Start();
            }
        }
    }

    private void Advance()
    {
        Array.Copy(_requests, 1, _requests, 0, HistoryLength - 1);
        Array.Copy(_errors, 1, _errors, 0, HistoryLength - 1);
        var previous = _requests[HistoryLength - 2];
        _requests[HistoryLength - 1] = Math.Clamp(previous + ((_random.NextDouble() - 0.5) * 30), 20, 180);
        _errors[HistoryLength - 1] = Math.Round(_random.NextDouble() * _requests[HistoryLength - 1] / 12, 1);
        _load = Math.Clamp(_load + ((_random.NextDouble() - 0.5) * 12), 5, 98);
    }

    private string CreateData()
    {
        static JsonArray Values(double[] samples)
        {
            var values = new JsonArray();
            foreach (var sample in samples)
            {
                values.Add((JsonNode)new JsonObject { ["y"] = Math.Round(sample, 1) });
            }

            return values;
        }

        var data = new JsonObject
        {
            ["traffic"] = new JsonArray
            {
                (JsonNode)new JsonObject { ["legend"] = "Requests", ["color"] = "categoricalBlue", ["values"] = Values(_requests) },
                (JsonNode)new JsonObject { ["legend"] = "Errors", ["color"] = "categoricalRed", ["values"] = Values(_errors) },
            },
            ["load"] = Math.Round(_load),
            ["loadColor"] = _load switch
            {
                >= 85 => "attention",
                >= 60 => "warning",
                _ => "good",
            },
        };

        return data.ToJsonString();
    }
}
