export default {
  preset: 'ts-jest',
  testEnvironment: 'node',
  passWithNoTests: true,
  testMatch: ['**/tests/**/*.test.ts'],
  collectCoverageFrom: ['src/**/*.ts'],
  coverageDirectory: 'coverage',
  moduleFileExtensions: ['ts', 'js', 'json', 'node'],
  testPathIgnorePatterns: ['/node_modules/', '/dist/', '/tests/fixture/']
};
