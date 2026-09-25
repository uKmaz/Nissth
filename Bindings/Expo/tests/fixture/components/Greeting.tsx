import { Text } from "react-native";
import { useMemo } from "react";

export interface GreetingProps {
  name: string;
}

export function Greeting({ name }: GreetingProps) {
  const message = useMemo(() => `Hello, ${name}!`, [name]);
  return <Text>{message}</Text>;
}
