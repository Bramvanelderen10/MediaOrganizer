import 'package:flutter/material.dart';
import 'di/service_locator.dart';
import 'screens/home/home_screen.dart';
import 'screens/setup/setup_screen.dart';
import 'services/api_service.dart';
import 'services/storage_service.dart';

void main() {
  setupServiceLocator();
  runApp(const MediaOrganizerApp());
}

class MediaOrganizerApp extends StatelessWidget {
  const MediaOrganizerApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Media Organizer',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorSchemeSeed: Colors.deepPurple,
        brightness: Brightness.light,
        useMaterial3: true,
      ),
      darkTheme: ThemeData(
        colorSchemeSeed: Colors.deepPurple,
        brightness: Brightness.dark,
        useMaterial3: true,
      ),
      home: const _EntryPoint(),
    );
  }
}

/// Checks for a saved API URL and routes to the right screen.
class _EntryPoint extends StatefulWidget {
  const _EntryPoint();

  @override
  State<_EntryPoint> createState() => _EntryPointState();
}

class _EntryPointState extends State<_EntryPoint> {
  final StorageService _storage = getIt<StorageService>();
  late Future<String?> _urlFuture;

  @override
  void initState() {
    super.initState();
    _urlFuture = _storage.getApiUrl();
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<String?>(
      future: _urlFuture,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Scaffold(
            body: Center(child: CircularProgressIndicator()),
          );
        }

        final url = snapshot.data;
        if (url != null) {
          registerApiService(url);
          return HomeScreen(api: getIt<ApiService>(), storage: _storage);
        }
        return SetupScreen(storage: _storage);
      },
    );
  }
}
