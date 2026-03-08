import 'package:shared_preferences/shared_preferences.dart';

/// Persists the API base URL using shared preferences.
class StorageService {
  static const _apiUrlKey = 'api_base_url';

  final SharedPreferencesAsync _prefs = SharedPreferencesAsync();

  /// Returns the saved API base URL, or `null` if not configured yet.
  Future<String?> getApiUrl() async {
    final url = await _prefs.getString(_apiUrlKey);
    return (url != null && url.isNotEmpty) ? url : null;
  }

  /// Saves the API base URL.
  Future<void> setApiUrl(String url) async {
    await _prefs.setString(_apiUrlKey, url);
  }

  /// Clears the saved API base URL.
  Future<void> clearApiUrl() async {
    await _prefs.remove(_apiUrlKey);
  }
}
