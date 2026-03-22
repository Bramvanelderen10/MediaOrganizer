import 'package:get_it/get_it.dart';

import '../services/api_service.dart';
import '../services/storage_service.dart';

final getIt = GetIt.instance;

void setupServiceLocator() {
  getIt.registerLazySingleton<StorageService>(() => StorageService());
}

void registerApiService(String baseUrl) {
  if (getIt.isRegistered<ApiService>()) {
    getIt.unregister<ApiService>();
  }
  getIt.registerSingleton<ApiService>(ApiService(baseUrl: baseUrl));
}

void unregisterApiService() {
  if (getIt.isRegistered<ApiService>()) {
    getIt.unregister<ApiService>();
  }
}
