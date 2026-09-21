import 'package:flutter/material.dart';

import '../../services/api_service.dart';
import '../../services/torrent_intent_service.dart';

/// Confirmation dialog shown when a `.torrent` file or magnet link is opened with the app.
///
/// The download only starts after the user confirms.
class TorrentConfirmDialog extends StatefulWidget {
  final ApiService api;
  final TorrentIntentRequest request;

  const TorrentConfirmDialog({
    super.key,
    required this.api,
    required this.request,
  });

  /// Shows the dialog and completes when it is dismissed.
  static Future<void> show(
    BuildContext context, {
    required ApiService api,
    required TorrentIntentRequest request,
  }) {
    return showDialog<void>(
      context: context,
      builder: (_) => TorrentConfirmDialog(api: api, request: request),
    );
  }

  @override
  State<TorrentConfirmDialog> createState() => _TorrentConfirmDialogState();
}

class _TorrentConfirmDialogState extends State<TorrentConfirmDialog> {
  bool _isUploading = false;
  String? _errorMessage;

  bool get _isMagnet => widget.request.magnetLink != null;

  String get _displayName => widget.request.displayName;

  /// Feature name used when explaining that the server is too old.
  String get _featureLabel => _isMagnet ? 'magnet links' : '.torrent files';

  Future<void> _startDownload() async {
    setState(() {
      _isUploading = true;
      _errorMessage = null;
    });

    try {
      final result = _isMagnet
          ? await widget.api.addMagnet(magnetLink: widget.request.magnetLink!)
          : await widget.api.addTorrent(filePath: widget.request.filePath!);

      if (!mounted) return;

      final savePath = result['savePath'] as String?;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            savePath == null
                ? 'Download started: $_displayName'
                : 'Download started: $_displayName → $savePath',
          ),
        ),
      );
    } on ApiException catch (ex) {
      if (!mounted) return;
      setState(() {
        _isUploading = false;
        _errorMessage = ex.userMessage(feature: _featureLabel);
      });
    } catch (ex) {
      if (!mounted) return;
      setState(() {
        _isUploading = false;
        _errorMessage = 'Could not start the download: $ex';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(_isMagnet ? 'Start magnet download?' : 'Start download?'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(_displayName, style: const TextStyle(fontWeight: FontWeight.bold)),
          const SizedBox(height: 12),
          Text(
            _isMagnet
                ? 'The magnet link will be added to qBittorrent and the download starts '
                      'immediately. Files land in the download folder configured on the server.'
                : 'The torrent will be added to qBittorrent and the download starts '
                      'immediately. Files land in the download folder configured on the server.',
          ),
          if (_isMagnet) ...[
            const SizedBox(height: 8),
            Text(
              'Metadata is fetched from peers first, so it may take a moment before '
              'the name and size appear.',
              style: TextStyle(
                fontSize: 12,
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
          ],
          if (_errorMessage != null) ...[
            const SizedBox(height: 12),
            Text(
              _errorMessage!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
        ],
      ),
      actions: [
        TextButton(
          onPressed: _isUploading ? null : () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: _isUploading ? null : _startDownload,
          child: _isUploading
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Text('Download'),
        ),
      ],
    );
  }
}
