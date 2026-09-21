import 'dart:async';

import 'package:flutter/material.dart';

import '../../services/api_service.dart';

/// Lists all torrents with their live download progress.
///
/// Polls the server while the screen is open so progress advances on its own.
class TorrentsScreen extends StatefulWidget {
  final ApiService api;

  /// Poll interval; overridable in tests.
  final Duration refreshInterval;

  const TorrentsScreen({
    super.key,
    required this.api,
    this.refreshInterval = const Duration(seconds: 3),
  });

  @override
  State<TorrentsScreen> createState() => _TorrentsScreenState();
}

class _TorrentsScreenState extends State<TorrentsScreen> {
  List<Map<String, dynamic>> _torrents = const [];
  Timer? _timer;
  bool _isLoading = true;
  bool _isRefreshing = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    unawaited(_load(initial: true));
    _timer = Timer.periodic(widget.refreshInterval, (_) {
      unawaited(_load());
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _timer = null;
    super.dispose();
  }

  Future<void> _load({bool initial = false}) async {
    if (_isRefreshing) {
      return;
    }
    _isRefreshing = true;

    try {
      final torrents = await widget.api.getTorrents();
      if (!mounted) return;

      setState(() {
        _torrents = torrents;
        _errorMessage = null;
        _isLoading = false;
      });
    } on ApiException catch (ex) {
      if (!mounted) return;
      setState(() {
        _errorMessage = ex.userMessage(feature: 'torrent list');
        _isLoading = false;
        if (ex.isNotFound || ex.isNotConfigured) {
          // No point polling an endpoint that is clearly unavailable.
          _torrents = const [];
        }
      });
    } catch (ex) {
      if (!mounted) return;
      setState(() {
        _errorMessage = 'Could not load torrents: $ex';
        _isLoading = false;
      });
    } finally {
      _isRefreshing = false;
      if (initial && mounted) {
        setState(() {});
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Torrents'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
            onPressed: () => unawaited(_load()),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => _load(),
        child: _buildBody(),
      ),
    );
  }

  Widget _buildBody() {
    if (_isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_errorMessage != null && _torrents.isEmpty) {
      return MessageView(
        icon: Icons.error_outline,
        title: 'Torrents unavailable',
        message: _errorMessage!,
        onRetry: () => unawaited(_load(initial: true)),
      );
    }

    if (_torrents.isEmpty) {
      return const MessageView(
        icon: Icons.download_done,
        title: 'No torrents',
        message:
            'Nothing is downloading. Open a .torrent file or magnet link to start one.',
      );
    }

    final hasStaleError = _errorMessage != null;

    return ListView.separated(
      physics: const AlwaysScrollableScrollPhysics(),
      itemCount: _torrents.length + (hasStaleError ? 1 : 0),
      separatorBuilder: (_, _) => const Divider(height: 1),
      itemBuilder: (context, index) {
        if (hasStaleError && index == 0) {
          return Container(
            color: Theme.of(context).colorScheme.errorContainer,
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                const Icon(Icons.warning_amber, size: 18),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Last refresh failed. Showing the previous data.',
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.onErrorContainer,
                      fontSize: 12,
                    ),
                  ),
                ),
              ],
            ),
          );
        }

        return TorrentTile(torrent: _torrents[index - (hasStaleError ? 1 : 0)]);
      },
    );
  }
}

/// A single torrent row with a progress bar and transfer details.
class TorrentTile extends StatelessWidget {
  final Map<String, dynamic> torrent;

  const TorrentTile({super.key, required this.torrent});

  @override
  Widget build(BuildContext context) {
    final name = (torrent['name'] as String?)?.trim();
    final status = (torrent['status'] as String?) ?? 'Unknown';
    final progress = (torrent['progress'] as num?)?.toDouble() ?? 0;
    final sizeBytes = (torrent['sizeBytes'] as num?)?.toInt() ?? 0;
    final downloadedBytes = (torrent['downloadedBytes'] as num?)?.toInt() ?? 0;
    final downloadSpeed = (torrent['downloadSpeed'] as num?)?.toInt() ?? 0;
    final uploadSpeed = (torrent['uploadSpeed'] as num?)?.toInt() ?? 0;
    final etaSeconds = (torrent['etaSeconds'] as num?)?.toInt();
    final savePath = (torrent['savePath'] as String?) ?? '';
    final isComplete = progress >= 0.999;

    final percent = (progress * 100).toStringAsFixed(isComplete ? 0 : 1);
    final scheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  (name == null || name.isEmpty) ? '(fetching metadata)' : name,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              const SizedBox(width: 8),
              Text(
                '$percent%',
                style: TextStyle(
                  fontWeight: FontWeight.bold,
                  color: isComplete ? Colors.green : scheme.primary,
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(
              value: progress.clamp(0.0, 1.0),
              minHeight: 6,
            ),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Icon(
                isComplete ? Icons.check_circle : Icons.downloading,
                size: 14,
                color: isComplete ? Colors.green : scheme.onSurfaceVariant,
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  status,
                  style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              if (!isComplete && etaSeconds != null && etaSeconds > 0)
                Text(
                  'ETA ${formatEta(etaSeconds)}',
                  style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
                ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            _transferSummary(
              downloadedBytes,
              sizeBytes,
              downloadSpeed,
              uploadSpeed,
            ),
            style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
          ),
          if (savePath.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 2),
              child: Text(
                savePath,
                style: TextStyle(fontSize: 11, color: scheme.outline),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
        ],
      ),
    );
  }

  static String _transferSummary(
    int downloadedBytes,
    int sizeBytes,
    int downloadSpeed,
    int uploadSpeed,
  ) {
    final buffer = StringBuffer()
      ..write(formatBytes(downloadedBytes))
      ..write(' / ')
      ..write(formatBytes(sizeBytes));

    if (downloadSpeed > 0) {
      buffer.write('  down ${formatBytes(downloadSpeed)}/s');
    }
    if (uploadSpeed > 0) {
      buffer.write('  up ${formatBytes(uploadSpeed)}/s');
    }

    return buffer.toString();
  }
}

/// Centered informational view used for empty and error states.
class MessageView extends StatelessWidget {
  final IconData icon;
  final String title;
  final String message;
  final VoidCallback? onRetry;

  const MessageView({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.onRetry,
  });

  @override
  Widget build(BuildContext context) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(32),
      children: [
        const SizedBox(height: 48),
        Icon(icon, size: 56, color: Theme.of(context).colorScheme.outline),
        const SizedBox(height: 16),
        Text(
          title,
          textAlign: TextAlign.center,
          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
        ),
        const SizedBox(height: 8),
        Text(
          message,
          textAlign: TextAlign.center,
          style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant),
        ),
        if (onRetry != null) ...[
          const SizedBox(height: 24),
          Center(
            child: FilledButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh),
              label: const Text('Retry'),
            ),
          ),
        ],
      ],
    );
  }
}

/// Formats a byte count using binary units, e.g. `1.4 GB`.
String formatBytes(num bytes) {
  if (bytes <= 0) return '0 B';

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  var value = bytes.toDouble();
  var unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }

  final digits = (value >= 100 || unit == 0) ? 0 : 1;
  return '${value.toStringAsFixed(digits)} ${units[unit]}';
}

/// Formats a duration in seconds as `1h 05m`, `12m 30s` or `45s`.
String formatEta(int seconds) {
  if (seconds <= 0) return '--';

  final hours = seconds ~/ 3600;
  final minutes = (seconds % 3600) ~/ 60;
  final remaining = seconds % 60;

  if (hours > 0) {
    return '${hours}h ${minutes.toString().padLeft(2, '0')}m';
  }
  if (minutes > 0) {
    return '${minutes}m ${remaining.toString().padLeft(2, '0')}s';
  }
  return '${remaining}s';
}