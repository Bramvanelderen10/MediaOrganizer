import 'dart:async';

import 'package:flutter/material.dart';

import '../../services/api_service.dart';

/// Shows the progress of the background transcode job and a hardware self-test.
class TranscodeJobScreen extends StatefulWidget {
  final ApiService api;

  const TranscodeJobScreen({super.key, required this.api});

  @override
  State<TranscodeJobScreen> createState() => _TranscodeJobScreenState();
}

class _TranscodeJobScreenState extends State<TranscodeJobScreen> {
  Timer? _timer;
  Map<String, dynamic>? _job;
  String? _error;
  bool _loading = true;

  bool _selfTestRunning = false;
  Map<String, dynamic>? _selfTest;

  @override
  void initState() {
    super.initState();
    _refresh();
    _timer = Timer.periodic(const Duration(seconds: 2), (_) => _refresh());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _refresh() async {
    try {
      final job = await widget.api.getTranscodeJob();
      if (!mounted) return;
      setState(() {
        _job = job;
        _error = null;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error =
            e.isNotFound
                ? 'This server does not support transcode jobs yet. '
                    'Update MediaOrganizer on the server.'
                : e.serverMessage ?? 'Failed to load the job (${e.statusCode}).';
        _loading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = 'Failed to load the job: $e';
        _loading = false;
      });
    }
  }

  Future<void> _runSelfTest() async {
    setState(() => _selfTestRunning = true);

    try {
      final result = await widget.api.runTranscodeSelfTest();
      if (!mounted) return;
      setState(() => _selfTest = result);
    } on ApiException catch (e) {
      if (!mounted) return;
      _showError(e.serverMessage ?? 'Self-test failed (${e.statusCode}).');
    } catch (e) {
      if (!mounted) return;
      _showError('Self-test failed: $e');
    } finally {
      if (mounted) setState(() => _selfTestRunning = false);
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message), backgroundColor: Colors.red),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Transcode job'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
            onPressed: _refresh,
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          if (_loading)
            const Center(
              child: Padding(
                padding: EdgeInsets.all(32),
                child: CircularProgressIndicator(),
              ),
            ),
          if (_error != null) _errorCard(context),
          if (_job != null) ..._jobSection(context),
          const SizedBox(height: 32),
          _selfTestSection(context),
        ],
      ),
    );
  }

  Widget _errorCard(BuildContext context) {
    return Card(
      color: Theme.of(context).colorScheme.errorContainer,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Text(_error!),
      ),
    );
  }

  List<Widget> _jobSection(BuildContext context) {
    final job = _job!;
    final state = (job['state'] as String?) ?? 'idle';
    final isRunning = job['isRunning'] == true;
    final total = _int(job['totalFiles']);
    final processed = _int(job['processedFiles']);
    final transcoded = _int(job['transcodedFiles']);
    final skipped = _int(job['skippedFiles']);
    final failed = _int(job['failedFiles']);
    final label = job['label'] as String?;
    final currentFile = job['currentFile'] as String?;
    final error = job['error'] as String?;
    final startedAt = job['startedAt'] as String?;
    final finishedAt = job['finishedAt'] as String?;

    final widgets = <Widget>[
      Row(
        children: [
          Expanded(
            child: Text(
              label == null || label.isEmpty ? 'Transcode job' : 'Transcode: $label',
              style: Theme.of(context).textTheme.titleLarge,
            ),
          ),
          _stateChip(state),
        ],
      ),
      const SizedBox(height: 16),
    ];

    if (state == 'idle') {
      widgets.add(
        const Text(
          'No transcode job has run yet. Use the Library screen buttons to start one.',
        ),
      );
      return widgets;
    }

    widgets.addAll([
      if (total > 0) ...[
        LinearProgressIndicator(
          value: isRunning ? processed / total : (state == 'completed' ? 1 : 0),
        ),
        const SizedBox(height: 8),
        Text('$processed of $total file(s) processed'),
        const SizedBox(height: 16),
      ],
      _countsRow(context, transcoded, skipped, failed),
      if (isRunning && currentFile != null) ...[
        const SizedBox(height: 16),
        Text('Current file', style: Theme.of(context).textTheme.labelLarge),
        Text(currentFile, maxLines: 2, overflow: TextOverflow.ellipsis),
      ],
      const SizedBox(height: 16),
      if (startedAt != null) Text('Started: ${_formatTime(startedAt)}'),
      if (finishedAt != null) Text('Finished: ${_formatTime(finishedAt)}'),
      if (error != null) ...[
        const SizedBox(height: 12),
        Text(error, style: TextStyle(color: Theme.of(context).colorScheme.error)),
      ],
    ]);

    return widgets;
  }

  Widget _stateChip(String state) {
    final (label, color) = switch (state) {
      'running' => ('Running', Colors.blue),
      'completed' => ('Completed', Colors.green),
      'failed' => ('Failed', Colors.red),
      _ => ('Idle', Colors.grey),
    };

    return Chip(
      label: Text(label, style: const TextStyle(color: Colors.white)),
      backgroundColor: color,
      visualDensity: VisualDensity.compact,
    );
  }

  Widget _countsRow(BuildContext context, int transcoded, int skipped, int failed) {
    return Wrap(
      spacing: 12,
      runSpacing: 8,
      children: [
        _countChip('Transcoded', transcoded, Colors.green),
        _countChip('Skipped', skipped, Colors.blueGrey),
        _countChip('Failed', failed, failed > 0 ? Colors.red : Colors.blueGrey),
      ],
    );
  }

  Widget _countChip(String label, int value, Color color) {
    return Chip(
      avatar: CircleAvatar(backgroundColor: color, radius: 10),
      label: Text('$label: $value'),
    );
  }

  Widget _selfTestSection(BuildContext context) {
    final result = _selfTest;
    final hardwareEncode = result?['hardwareEncode'] == true;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Hardware self-test', style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 8),
        const Text(
          'Runs a 2 second test encode with the configured encoder to prove the GPU driver '
          'works, instead of only checking that the encoder exists.',
        ),
        const SizedBox(height: 12),
        SizedBox(
          height: 48,
          child: FilledButton.icon(
            onPressed: _selfTestRunning ? null : _runSelfTest,
            icon:
                _selfTestRunning
                    ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Colors.white,
                      ),
                    )
                    : const Icon(Icons.science_outlined),
            label: Text(_selfTestRunning ? 'Testing…' : 'Run self-test'),
          ),
        ),
        if (result != null) ...[
          const SizedBox(height: 16),
          Card(
            color:
                hardwareEncode
                    ? Colors.green.shade50
                    : Theme.of(context).colorScheme.errorContainer,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    hardwareEncode
                        ? 'Hardware encoding is working'
                        : 'Hardware encoding is NOT working (would fall back to software)',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  if (result['hint'] is String &&
                      (result['hint'] as String).isNotEmpty) ...[
                    const SizedBox(height: 8),
                    Text(
                      result['hint'] as String,
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ],
                  const SizedBox(height: 8),
                  Text('Encoder: ${result['encoder'] ?? '-'}'),
                  Text('Driver: ${result['driver'] ?? 'unknown'}'),
                  Text(
                    'Encoder available: '
                    '${result['encoderAvailable'] == true ? 'yes' : 'no'}',
                  ),
                  Text(
                    'Test encode succeeded: '
                    '${result['encodeSucceeded'] == true ? 'yes' : 'no'}',
                  ),
                  if (result['output'] is String &&
                      (result['output'] as String).isNotEmpty) ...[
                    const SizedBox(height: 12),
                    Text(
                      'ffmpeg output',
                      style: Theme.of(context).textTheme.labelLarge,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      result['output'] as String,
                      style: const TextStyle(
                        fontFamily: 'monospace',
                        fontSize: 12,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ],
    );
  }

  static int _int(Object? value) => (value as num?)?.toInt() ?? 0;

  static String _formatTime(String iso) {
    final parsed = DateTime.tryParse(iso);
    if (parsed == null) return iso;

    final local = parsed.toLocal();
    return '${local.year}-${local.month.toString().padLeft(2, '0')}-'
        '${local.day.toString().padLeft(2, '0')} '
        '${local.hour.toString().padLeft(2, '0')}:'
        '${local.minute.toString().padLeft(2, '0')}:'
        '${local.second.toString().padLeft(2, '0')}';
  }
}