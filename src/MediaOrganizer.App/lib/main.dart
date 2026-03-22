import 'package:flutter/material.dart';
import 'screens/home/home_screen.dart';
import 'screens/setup/setup_screen.dart';
import 'services/storage_service.dart';

void main() {
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
  late Future<String?> _urlFuture;

  @override
  void initState() {
    super.initState();
    _urlFuture = StorageService().getApiUrl();
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
          return HomeScreen(apiUrl: url);
        }
        return const SetupScreen();
      },
    );
  }
}
