import 'package:flutter/material.dart';
import 'dart:async';
import '../../di/service_locator.dart';
import '../../services/api_service.dart';
import '../../services/storage_service.dart';
import '../setup/setup_screen.dart';
import '../file_browser/file_browser_screen.dart';
import '../library/library_screen.dart';
import '../storage/storage_screen.dart';
import 'widgets/api_status_header.dart';
import 'widgets/forget_season_dialog.dart';
import 'widgets/log_stream_container.dart';
import 'widgets/organize_button.dart';

enum _AppMenuAction {
  library,
  fileBrowser,
  storage,
  forgetShowSeason,
  resetApiUrl,
}

/// Main screen with a single button to trigger the organize job.
class HomeScreen extends StatefulWidget {
  final ApiService api;
  final StorageService storage;

  const HomeScreen({super.key, required this.api, required this.storage});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> with WidgetsBindingObserver {
  late final ApiService _api;
  bool _isLoading = false;

  Timer? _healthTimer;
  bool _isApiHealthy = false;
  bool _isCheckingHealth = false;
  String? _apiUnavailableMessage;

  StreamSubscription<String>? _logSubscription;
  final List<String> _logLines = <String>[];
  final ScrollController _logScrollController = ScrollController();
  bool _isLogConnecting = false;
  bool _isLogConnected = false;
  String? _logError;
  DateTime _nextLogConnectAttemptAt = DateTime.fromMillisecondsSinceEpoch(0);

  @override
  void initState() {
    super.initState();
    _api = widget.api;

    WidgetsBinding.instance.addObserver(this);
    _startHealthPolling();
  }

  @override
  void dispose() {
    _stopHealthPolling();
    _disconnectLogs(updateUi: false);
    _logScrollController.dispose();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    switch (state) {
      case AppLifecycleState.resumed:
        _startHealthPolling();
        break;
      case AppLifecycleState.inactive:
      case AppLifecycleState.paused:
      case AppLifecycleState.detached:
      case AppLifecycleState.hidden:
        _stopHealthPolling();
        _disconnectLogs();
        break;
    }
  }

  void _startHealthPolling() {
    if (_healthTimer != null) return;

    // Run once immediately, then poll every second while the app is open.
    unawaited(_refreshApiHealth());
    _healthTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      unawaited(_refreshApiHealth());
    });
  }

  void _stopHealthPolling() {
    _healthTimer?.cancel();
    _healthTimer = null;
  }

  Future<void> _refreshApiHealth() async {
    if (_isCheckingHealth) return;
    _isCheckingHealth = true;

    try {
      final reachable = await _api.healthCheck(
        timeout: const Duration(milliseconds: 800),
      );
      if (!mounted) return;

      final message =
          reachable
              ? null
              : 'API not available. Make sure the server is running.';

      if (reachable != _isApiHealthy || message != _apiUnavailableMessage) {
        setState(() {
          _isApiHealthy = reachable;
          _apiUnavailableMessage = message;
        });
      }

      // Auto-connect logs while the API is reachable.
      if (reachable) {
        unawaited(_maybeAutoConnectLogs());
      } else {
        _disconnectLogs();
      }
    } finally {
      _isCheckingHealth = false;
    }
  }

  Future<void> _maybeAutoConnectLogs() async {
    if (!_isApiHealthy) return;
    if (_isLogConnected || _isLogConnecting) return;

    final now = DateTime.now();
    if (now.isBefore(_nextLogConnectAttemptAt)) return;

    _nextLogConnectAttemptAt = now.add(const Duration(seconds: 5));
    await _connectLogs();
  }

  Future<void> _connectLogs() async {
    if (_isLogConnecting || _isLogConnected) return;
    if (!_isApiHealthy) return;

    setState(() {
      _isLogConnecting = true;
      _logError = null;
    });

    try {
      final stream = _api.streamLogs(tail: 200);
      _logSubscription = stream.listen(
        (line) {
          if (!mounted) return;
          setState(() {
            _isLogConnected = true;
            _logLines.add(line);
            // Keep memory bounded.
            if (_logLines.length > 500) {
              _logLines.removeRange(0, _logLines.length - 500);
            }
          });

          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (!_logScrollController.hasClients) return;
            final pos = _logScrollController.position;
            _logScrollController.jumpTo(pos.maxScrollExtent);
          });
        },
        onError: (Object e) {
          if (!mounted) return;
          setState(() {
            _logError = e.toString();
            _isLogConnected = false;
          });
        },
        onDone: () {
          if (!mounted) return;
          setState(() {
            _isLogConnected = false;
          });
        },
        cancelOnError: false,
      );
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _logError = e.toString();
        _isLogConnected = false;
      });
    } finally {
      if (mounted) {
        setState(() {
          _isLogConnecting = false;
        });
      }
    }
  }

  void _disconnectLogs({bool updateUi = true}) {
    _logSubscription?.cancel();
    _logSubscription = null;

    if (updateUi && mounted) {
      setState(() {
        _isLogConnecting = false;
        _isLogConnected = false;
      });
      return;
    }

    _isLogConnecting = false;
    _isLogConnected = false;
  }

  Future<void> _triggerOrganize() async {
    setState(() => _isLoading = true);

    try {
      await _api.triggerJob();
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Organize job triggered successfully!'),
          backgroundColor: Colors.green,
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Failed to trigger job: $e'),
          backgroundColor: Colors.red,
        ),
      );
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _resetConfig() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder:
          (ctx) => AlertDialog(
            title: const Text('Reset configuration?'),
            content: const Text(
              'This will clear the saved API URL and return to setup.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx, false),
                child: const Text('Cancel'),
              ),
              TextButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('Reset'),
              ),
            ],
          ),
    );

    if (confirmed == true) {
      await widget.storage.clearApiUrl();
      unregisterApiService();
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => SetupScreen(storage: widget.storage)),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Media Organizer'),
        actions: [
          PopupMenuButton<_AppMenuAction>(
            tooltip: 'Menu',
            onSelected: (action) {
              switch (action) {
                case _AppMenuAction.library:
                  Navigator.of(context).push(
                    MaterialPageRoute(builder: (_) => LibraryScreen(api: _api)),
                  );
                  break;
                case _AppMenuAction.fileBrowser:
                  Navigator.of(context).push(
                    MaterialPageRoute(
                      builder: (_) => FileBrowserScreen(api: _api),
                    ),
                  );
                  break;
                case _AppMenuAction.storage:
                  Navigator.of(context).push(
                    MaterialPageRoute(builder: (_) => StorageScreen(api: _api)),
                  );
                  break;
                case _AppMenuAction.forgetShowSeason:
                  unawaited(ForgetSeasonDialog.show(context, _api));
                  break;
                case _AppMenuAction.resetApiUrl:
                  unawaited(_resetConfig());
                  break;
              }
            },
            itemBuilder:
                (context) => const [
                  PopupMenuItem<_AppMenuAction>(
                    value: _AppMenuAction.library,
                    child: Text('Library'),
                  ),
                  PopupMenuItem<_AppMenuAction>(
                    value: _AppMenuAction.fileBrowser,
                    child: Text('Source Files'),
                  ),
                  PopupMenuItem<_AppMenuAction>(
                    value: _AppMenuAction.storage,
                    child: Text('Storage'),
                  ),
                  PopupMenuItem<_AppMenuAction>(
                    value: _AppMenuAction.forgetShowSeason,
                    child: Text('Forget show season'),
                  ),
                  PopupMenuItem<_AppMenuAction>(
                    value: _AppMenuAction.resetApiUrl,
                    child: Text('Reset API URL'),
                  ),
                ],
            icon: const Icon(Icons.more_vert),
          ),
        ],
      ),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 640),
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(32),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ApiStatusHeader(apiUrl: _api.baseUrl),
                  const SizedBox(height: 48),
                  OrganizeButton(
                    isLoading: _isLoading,
                    isApiHealthy: _isApiHealthy,
                    apiUnavailableMessage: _apiUnavailableMessage,
                    onPressed: _triggerOrganize,
                  ),
                  const SizedBox(height: 16),
                  LogStreamContainer(
                    isApiHealthy: _isApiHealthy,
                    isLogConnecting: _isLogConnecting,
                    isLogConnected: _isLogConnected,
                    logLines: _logLines,
                    logError: _logError,
                    scrollController: _logScrollController,
                    onConnect: () {
                      unawaited(_connectLogs());
                    },
                    onDisconnect: _disconnectLogs,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
