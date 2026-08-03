// https://docs.expo.dev/guides/using-eslint/
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');

module.exports = defineConfig([
  expoConfig,
  {
    ignores: ['dist/*', '.expo/**'],
  },
  {
    // jest.mock() factories can't reference top-level imports — babel-plugin-jest-hoist
    // rejects any out-of-scope variable at compile time — so these files intentionally
    // `require()` react/react-native fresh inside the factory instead of importing them.
    files: ['**/*.test.ts', '**/*.test.tsx'],
    rules: {
      '@typescript-eslint/no-require-imports': 'off',
    },
  },
]);
