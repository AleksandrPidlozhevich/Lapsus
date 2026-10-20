import Foundation
import StoreKit

public typealias LapsusReply = @convention(c) (Int64, UnsafePointer<CChar>?) -> Void

private func reply(_ callback: LapsusReply, _ token: Int64, _ object: [String: Any]) {
    let data = (try? JSONSerialization.data(withJSONObject: object)) ?? Data("{}".utf8)
    String(decoding: data, as: UTF8.self).withCString { callback(token, $0) }
}

// A dismissed sheet arrives as StoreKitError, SKError, Cocoa/URL cancel, or ASDErrorDomain 907.
private func isCancellation(_ error: Error) -> Bool {
    if let storeKit = error as? StoreKitError, case .userCancelled = storeKit {
        return true
    }
    if let payment = error as? SKError, payment.code == .paymentCancelled {
        return true
    }
    let ns = error as NSError
    return (ns.domain == NSCocoaErrorDomain && ns.code == NSUserCancelledError)
        || (ns.domain == NSURLErrorDomain && ns.code == NSURLErrorCancelled)
        || (ns.domain == "ASDErrorDomain" && ns.code == 907)
}

private func ownedProducts() async -> [[String: Any]] {
    var owned: [[String: Any]] = []
    for await result in Transaction.currentEntitlements {
        guard case .verified(let transaction) = result, transaction.revocationDate == nil else { continue }
        var item: [String: Any] = ["productId": transaction.productID]
        if let expires = transaction.expirationDate {
            item["expires"] = expires.timeIntervalSince1970
        }
        owned.append(item)
    }
    return owned
}

@_cdecl("lapsus_store_owned")
public func lapsusStoreOwned(_ token: Int64, _ callback: LapsusReply) {
    Task {
        reply(callback, token, ["owned": await ownedProducts()])
    }
}

@_cdecl("lapsus_store_products")
public func lapsusStoreProducts(_ token: Int64, _ ids: UnsafePointer<CChar>, _ callback: LapsusReply) {
    let wanted = String(cString: ids).split(separator: ",").map(String.init)
    Task {
        do {
            let products = try await Product.products(for: wanted)
            reply(callback, token, ["products": products.map { ["id": $0.id, "price": $0.displayPrice] }])
        } catch {
            reply(callback, token, ["error": error.localizedDescription])
        }
    }
}

@_cdecl("lapsus_store_purchase")
public func lapsusStorePurchase(_ token: Int64, _ id: UnsafePointer<CChar>, _ callback: LapsusReply) {
    let productId = String(cString: id)
    Task {
        do {
            guard let product = try await Product.products(for: [productId]).first else {
                reply(callback, token, ["status": "unavailable"])
                return
            }

            switch try await product.purchase() {
            case .success(let verification):
                guard case .verified(let transaction) = verification else {
                    reply(callback, token, ["status": "unverified"])
                    return
                }
                await transaction.finish()
                reply(callback, token, ["status": "purchased"])
            case .userCancelled:
                reply(callback, token, ["status": "cancelled"])
            case .pending:
                reply(callback, token, ["status": "pending"])
            @unknown default:
                reply(callback, token, ["status": "failed"])
            }
        } catch where isCancellation(error) {
            reply(callback, token, ["status": "cancelled"])
        } catch {
            reply(callback, token, ["status": "failed", "error": error.localizedDescription])
        }
    }
}

@_cdecl("lapsus_store_restore")
public func lapsusStoreRestore(_ token: Int64, _ callback: LapsusReply) {
    Task {
        do {
            try await AppStore.sync()
        } catch where isCancellation(error) {
            reply(callback, token, ["owned": await ownedProducts(), "cancelled": true])
            return
        } catch {
            reply(callback, token, ["error": error.localizedDescription])
            return
        }
        reply(callback, token, ["owned": await ownedProducts()])
    }
}

private let watchLock = NSLock()
private var watching = false

// Token 0: an update arrived on its own, so the app re-reads what is owned.
@_cdecl("lapsus_store_watch")
public func lapsusStoreWatch(_ callback: LapsusReply) {
    watchLock.lock()
    defer { watchLock.unlock() }
    if watching { return }
    watching = true

    Task.detached {
        for await result in Transaction.updates {
            if case .verified(let transaction) = result {
                await transaction.finish()
            }
            reply(callback, 0, ["changed": true])
        }
    }
}
