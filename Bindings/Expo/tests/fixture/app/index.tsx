import { View, Text } from "react-native";
import { Greeting } from "../components/Greeting";

export default function IndexScreen() {
  return (
    <View>
      <Text>Nissth Expo Fixture</Text>
      <Greeting name="Bridge" />
    </View>
  );
}
