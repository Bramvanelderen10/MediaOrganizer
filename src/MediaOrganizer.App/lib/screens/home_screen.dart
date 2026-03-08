import 'package:flutter/material.dart';
import 'dart:async';
import '../services/api_service.dart';
import '../services/storage_service.dart';
import 'setup_screen.dart';

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
    } finally {
      _isCheckingHealth = false;
    }
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

  @override
  Widget build(BuildContext context) {
    final canOrganize = !_isLoading && _isApiHealthy;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Media Organizer'),
        actions: [
          IconButton(
            icon: const Icon(Icons.settings),
            tooltip: 'Reset API URL',
            onPressed: _resetConfig,
          ),
        ],
      ),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.video_library_rounded,
                size: 80,
                color: Theme.of(context).colorScheme.primary,
              ),
              const SizedBox(height: 24),
              Text(
                'Connected to',
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: Colors.grey,
                ),
              ),
              Text(
                widget.apiUrl,
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
            ],
          ),
        ),
      ),
    );
  }
}
