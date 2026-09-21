import 'package:flutter_test/flutter_test.dart';

import 'package:media_organizer_app/screens/torrent/torrents_screen.dart';
import 'package:media_organizer_app/services/torrent_intent_service.dart';

void main() {
  group('TorrentIntentRequest', () {
    test('uses file name for .torrent files', () {
      const request = TorrentIntentRequest.file('/tmp/cache/Some.Movie.torrent');
      expect(request.displayName, 'Some.Movie.torrent');
      expect(request.magnetLink, isNull);
      expect(request.filePath, isNotNull);
    });

    test('uses the dn parameter for magnet links', () {
      const request = TorrentIntentRequest.magnet(
        'magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Some.Movie.2024',
      );
      expect(request.displayName, 'Some.Movie.2024');
      expect(request.filePath, isNull);
    });

    test('decodes percent-encoded dn values', () {
      const request = TorrentIntentRequest.magnet(
        'magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Some%20Movie%202024',
      );
      expect(request.displayName, 'Some Movie 2024');
    });

    test('falls back to the info hash when dn is missing', () {
      const request = TorrentIntentRequest.magnet(
        'magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567',
      );
      expect(request.displayName, contains('0123456789abcdef'));
    });

    test('falls back to a generic label when there is no hash', () {
      const request = TorrentIntentRequest.magnet('magnet:?dn=OnlyName');
      expect(request.displayName, 'OnlyName');
    });
  });

  group('formatBytes', () {
    test('formats plain bytes', () {
      expect(formatBytes(0), '0 B');
      expect(formatBytes(512), '512 B');
    });

    test('formats binary units', () {
      expect(formatBytes(1024), '1.0 KB');
      expect(formatBytes(1024 * 1024 * 1024), '1.0 GB');
    });

    test('omits decimals for large values', () {
      expect(formatBytes(500 * 1024 * 1024), '500 MB');
    });
  });

  group('formatEta', () {
    test('formats seconds', () {
      expect(formatEta(45), '45s');
    });

    test('formats minutes and seconds', () {
      expect(formatEta(125), '2m 05s');
    });

    test('formats hours and minutes', () {
      expect(formatEta(3900), '1h 05m');
    });

    test('handles unknown and negative values', () {
      expect(formatEta(0), '--');
      expect(formatEta(-1), '--');
    });
  });
}