import 'dart:async';

import 'package:receive_sharing_intent/receive_sharing_intent.dart';

/// Surfaces `.torrent` files that were opened with, or shared into, the app.
///
/// Intents can arrive before the UI is ready (cold start), so received paths are
/// queued until a handler is registered via [setHandler].
class TorrentIntentService {
  final List<String> _pending = [];

  StreamSubscription<List<SharedMediaFile>>? _subscription;
  void Function(String filePath)? _handler;
  bool _started = false;

  /// Begins listening for incoming file intents. Safe to call more than once.
  Future<void> start() async {
    if (_started) {
      return;
    }
    _started = true;

    try {
      _subscription = ReceiveSharingIntent.instance.getMediaStream().listen(
        _handleFiles,
        onError: (Object _) {
          // Opening files is best effort; ignore sharing errors.
        },
      );

      // Covers the case where the app was launched by opening a .torrent file.
      final initial = await ReceiveSharingIntent.instance.getInitialMedia();
      _handleFiles(initial);
      ReceiveSharingIntent.instance.reset();
    } catch (_) {
      // The plugin is only available on Android and iOS.
    }
  }

  /// Registers the handler that receives torrent paths and flushes anything
  /// that arrived before the handler was set.
  void setHandler(void Function(String filePath) handler) {
    _handler = handler;

    if (_pending.isEmpty) {
      return;
    }

    final queued = List<String>.from(_pending);
    _pending.clear();

    for (final path in queued) {
      handler(path);
    }
  }

  /// Removes the current handler.
  void clearHandler() {
    _handler = null;
  }

  void _handleFiles(List<SharedMediaFile> files) {
    for (final file in files) {
      if (!_isTorrent(file)) {
        continue;
      }

      final handler = _handler;
      if (handler != null) {
        handler(file.path);
      } else {
        _pending.add(file.path);
      }
    }
  }

  static bool _isTorrent(SharedMediaFile file) {
    final name = file.path.split('/').last.toLowerCase();
    return name.endsWith('.torrent');
  }

  Future<void> dispose() async {
    await _subscription?.cancel();
    _subscription = null;
    _handler = null;
    _pending.clear();
    _started = false;
  }
}
