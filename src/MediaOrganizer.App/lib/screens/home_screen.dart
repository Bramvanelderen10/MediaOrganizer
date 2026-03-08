import 'package:flutter/material.dart';
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

class _HomeScreenState extends State<HomeScreen> {
  late final ApiService _api;
  bool _isLoading = false;

  @override
  void initState() {
    super.initState();
    _api = ApiService(baseUrl: widget.apiUrl);
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
                  onPressed: _isLoading ? null : _triggerOrganize,
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
            ],
          ),
        ),
      ),
    );
  }
}
