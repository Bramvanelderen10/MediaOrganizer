import 'package:flutter/material.dart';
import 'dart:async';
import '../services/api_service.dart';
import '../services/storage_service.dart';
import 'setup_screen.dart';

enum _AppMenuAction {
  forgetShowSeason,
  resetApiUrl,
}

/// Main screen with a single button to trigger the organize job.
class HomeScreen extends StatefulWidget {
  final String apiUrl;

  const HomeScreen({super.key, required this.apiUrl});

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
    _api = ApiService(baseUrl: widget.apiUrl);

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

      final message = reachable
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

  Widget _buildLogPanel(BuildContext context) {
    final canToggle = _isApiHealthy && !_isLogConnecting;
    final statusText = _isLogConnecting
        ? 'Connecting…'
        : _isLogConnected
            ? 'Connected'
            : 'Disconnected';

    return Card(
      elevation: 0,
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Text(
                  'Live logs',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(width: 12),
                Text(
                  statusText,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: _isLogConnected
                            ? Colors.green
                            : Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                ),
                const Spacer(),
                if (_isLogConnecting)
                  const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                IconButton(
                  tooltip: _isLogConnected ? 'Disconnect logs' : 'Connect logs',
                  onPressed: canToggle
                      ? () {
                          if (_isLogConnected) {
                            _disconnectLogs();
                          } else {
                            unawaited(_connectLogs());
                          }
                        }
                      : null,
                  icon: Icon(
                    _isLogConnected
                        ? Icons.stop_circle_outlined
                        : Icons.play_circle_outline,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Container(
              height: 240,
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: Colors.black,
                borderRadius: BorderRadius.circular(8),
              ),
              child: _logLines.isEmpty
                  ? const Center(
                      child: Text(
                        'No logs yet…',
                        style: TextStyle(
                          color: Colors.white70,
                          fontFamily: 'monospace',
                          fontSize: 12,
                        ),
                      ),
                    )
                  : Scrollbar(
                      child: ListView.builder(
                        controller: _logScrollController,
                        itemCount: _logLines.length,
                        itemBuilder: (ctx, i) => Text(
                          _logLines[i],
                          style: const TextStyle(
                            color: Colors.white,
                            fontFamily: 'monospace',
                            fontSize: 12,
                            height: 1.25,
                          ),
                        ),
                      ),
                    ),
            ),
            if (_logError != null) ...[
              const SizedBox(height: 8),
              Text(
                _logError!,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: Theme.of(context).colorScheme.error,
                    ),
              ),
            ],
          ],
        ),
      ),
    );
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
      builder: (ctx) => AlertDialog(
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
      await StorageService().clearApiUrl();
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => const SetupScreen()),
      );
    }
  }

  Future<void> _showForgetSeasonDialog() async {
    final showController = TextEditingController();
    final seasonController = TextEditingController();

    try {
      await showDialog<void>(
        context: context,
        builder: (ctx) {
          var isSubmitting = false;
          String? errorText;

          return StatefulBuilder(
            builder: (context, setDialogState) => AlertDialog(
              title: const Text('Forget show season'),
              content: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  TextField(
                    controller: showController,
                    enabled: !isSubmitting,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Show name',
                      hintText: 'Sousou no Frieren',
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: seasonController,
                    enabled: !isSubmitting,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'Season number',
                      hintText: '2',
                    ),
                  ),
                  if (errorText != null) ...[
                    const SizedBox(height: 10),
                    Text(
                      errorText!,
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                            color: Theme.of(context).colorScheme.error,
                          ),
                    ),
                  ],
                ],
              ),
              actions: [
                TextButton(
                  onPressed: isSubmitting ? null : () => Navigator.pop(ctx),
                  child: const Text('Cancel'),
                ),
                FilledButton(
                  onPressed: isSubmitting
                      ? null
                      : () async {
                          final showName = showController.text.trim();
                          final seasonNumber = int.tryParse(
                            seasonController.text.trim(),
                          );

                          if (showName.isEmpty) {
                            setDialogState(() {
                              errorText = 'Show name is required.';
                            });
                            return;
                          }

                          if (seasonNumber == null || seasonNumber <= 0) {
                            setDialogState(() {
                              errorText = 'Season number must be greater than 0.';
                            });
                            return;
                          }

                          setDialogState(() {
                            isSubmitting = true;
                            errorText = null;
                          });

                          try {
                            await _api.forgetShowSeason(
                              showName: showName,
                              seasonNumber: seasonNumber,
                            );

                            if (!mounted) return;
                            if (ctx.mounted) {
                              Navigator.pop(ctx);
                            }

                            ScaffoldMessenger.of(this.context).showSnackBar(
                              SnackBar(
                                content: Text(
                                  'Forgot history for "$showName" season $seasonNumber.',
                                ),
                                backgroundColor: Colors.green,
                              ),
                            );
                          } catch (e) {
                            setDialogState(() {
                              isSubmitting = false;
                              errorText = 'Failed to forget season: $e';
                            });
                          }
                        },
                  child: isSubmitting
                      ? const SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('Forget'),
                ),
              ],
            ),
          );
        },
      );
    } finally {
      showController.dispose();
      seasonController.dispose();
    }
  }

  @override
  Widget build(BuildContext context) {
    final canOrganize = !_isLoading && _isApiHealthy;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Media Organizer'),
        actions: [
          PopupMenuButton<_AppMenuAction>(
            tooltip: 'Menu',
            onSelected: (action) {
              switch (action) {
                case _AppMenuAction.forgetShowSeason:
                  unawaited(_showForgetSeasonDialog());
                  break;
                case _AppMenuAction.resetApiUrl:
                  unawaited(_resetConfig());
                  break;
              }
            },
            itemBuilder: (context) => const [
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
                  Icon(
                    Icons.video_library_rounded,
                    size: 80,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                  const SizedBox(height: 24),
                  Text(
                    'Connected to',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: Colors.grey,
                        ),
                  ),
                  Text(
                    widget.apiUrl,
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyLarge,
                  ),
                  const SizedBox(height: 48),
                  SizedBox(
                    width: double.infinity,
                    height: 56,
                    child: FilledButton.icon(
                      onPressed: canOrganize ? _triggerOrganize : null,
                      icon: _isLoading
                          ? const SizedBox(
                              width: 20,
                              height: 20,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                color: Colors.white,
                              ),
                            )
                          : const Icon(Icons.play_arrow_rounded),
                      label: Text(
                        _isLoading ? 'Organizing…' : 'Organize videos',
                        style: const TextStyle(fontSize: 18),
                      ),
                    ),
                  ),
                  if (!_isApiHealthy && !_isLoading) ...[
                    const SizedBox(height: 12),
                    Text(
                      _apiUnavailableMessage ?? 'API not available.',
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                            color: Theme.of(context).colorScheme.error,
                          ),
                    ),
                  ],
                  const SizedBox(height: 16),
                  _buildLogPanel(context),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
