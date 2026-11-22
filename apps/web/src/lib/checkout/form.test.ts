import { describe, expect, it } from "vitest";
import { addressFieldOf, addressFrom, deliveryDays, keepMethod, methodsFor, type ShippingOption } from "./form";

const method = (id: string, countries: string[], isPickup = false): ShippingOption => ({
  id,
  name: id,
  description: null,
  isPickup,
  countries,
  minDays: null,
  maxDays: null,
  cost: 0,
});

describe("checkout form helpers", () => {
  it("offers the methods that ship to a country, pickup always", () => {
    const methods = [method("post", []), method("dhl", ["DE"]), method("shop", [], true), method("tipax", ["IR"])];
    expect(methodsFor(methods, "IR").map((m) => m.id)).toEqual(["post", "shop", "tipax"]);
    expect(methodsFor(methods, "DE").map((m) => m.id)).toEqual(["post", "dhl", "shop"]);
  });

  it("keeps the chosen method while it is offered, else takes the first", () => {
    const methods = [method("post", []), method("shop", [], true)];
    expect(keepMethod(methods, "shop")).toBe("shop");
    expect(keepMethod(methods, "gone")).toBe("post");
    expect(keepMethod([], "post")).toBeNull();
  });

  it("words the delivery time", () => {
    const texts = { days: "{min}–{max} days", daysOne: "{days} days" };
    expect(deliveryDays(2, 4, texts)).toBe("2–4 days");
    expect(deliveryDays(3, 3, texts)).toBe("3 days");
    expect(deliveryDays(null, 5, texts)).toBe("5 days");
    expect(deliveryDays(null, null, texts)).toBeNull();
  });

  it("reads the typed address and maps the API's nested field names", () => {
    const form = new FormData();
    form.set("address.recipientName", " Sara ");
    form.set("address.phone", "0912");
    form.set("address.countryCode", "IR");
    form.set("address.city", "Tehran");
    form.set("address.line1", "Valiasr 10");
    form.set("address.region", "");
    expect(addressFrom(form)).toEqual({
      label: null,
      recipientName: "Sara",
      phone: "0912",
      countryCode: "IR",
      region: null,
      city: "Tehran",
      line1: "Valiasr 10",
      line2: null,
      postalCode: null,
      isDefault: false,
    });
    expect(addressFieldOf("address.City")).toBe("city");
    expect(addressFieldOf("address.RecipientName")).toBe("recipientName");
    expect(addressFieldOf("email")).toBeNull();
  });
});
