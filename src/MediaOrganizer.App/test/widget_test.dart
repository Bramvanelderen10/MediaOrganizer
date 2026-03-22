import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:media_organizer_app/screens/setup/setup_screen.dart';

void main() {
  testWidgets('Setup screen renders', (WidgetTester tester) async {
    await tester.pumpWidget(const MaterialApp(home: SetupScreen()));

    expect(find.text('Media Organizer'), findsOneWidget);
    expect(
      find.text('Enter the address of your MediaOrganizer API to get started.'),
      findsOneWidget,
    );
  });
}
