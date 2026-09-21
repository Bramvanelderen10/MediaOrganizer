import 'package:flutter/material.dart';

import '../../services/api_service.dart';

/// Confirmation dialog shown when a `.torrent` file is opened with the app.
///
/// The download only starts after the user confirms.
class TorrentConfirmDialog extends StatefulWidget {
  final ApiService api;
  final String filePath;

  const TorrentConfirmDialog({
    super.key,
    required this.api,
    required this.filePath,
  });

  /// Shows the dialog and completes when it is dismissed.
  static Future<void> show(
    BuildContext context, {
    required ApiService api,
    required String filePath,
  }) {
    return showDialog<void>(
      context: context,
      builder: (_) => TorrentConfirmDialog(api: api, filePath: filePath),
    );
  }

  @override
  State<TorrentConfirmDialog> createState() => _TorrentConfirmDialogState();
}

class _TorrentConfirmDialogState extends State<TorrentConfirmDialog> {
  bool _isUploading = false;
  String? _errorMessage;

  String get _fileName => widget.filePath.split('/').last;

  Future<void> _startDownload() async {
    setState(() {
      _isUploading = true;
      _errorMessage = null;
    });

    try {
      final result = await widget.api.addTorrent(filePath: widget.filePath);
      if (!mounted) return;

      final savePath = result['savePath'] as String?;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            savePath == null
                ? 'Download started: $_fileName'
                : 'Download started: $_fileName → $savePath',
          ),
        ),
      );
    } on ApiException catch (ex) {
      if (!mounted) return;
      setState(() {
        _isUploading = false;
        _errorMessage = ex.statusCode == 503
            ? 'Torrent downloads are not configured on the server.'
            : ex.body;
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
      title: const Text('Start download?'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(_fileName, style: const TextStyle(fontWeight: FontWeight.bold)),
          const SizedBox(height: 12),
          const Text(
            'The torrent will be added to qBittorrent and the download starts '
            'immediately. Files land in the download folder configured on the server.',
          ),
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
