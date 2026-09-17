using System;
using NUnit.Framework;
using WakeQuery.Testing;

namespace WakeQuery.Tests
{
    public sealed class QueryKeyTests
    {
        [Test]
        public void EqualPrimitivePartsProduceEqualKeysAndHashes()
        {
            QueryKey<string> first = QueryKey.For<string>(
                "player",
                QueryKeyPart.Text("42"),
                QueryKeyPart.Signed(7),
                QueryKeyPart.Boolean(true));
            QueryKey<string> second = QueryKey.For<string>(
                "player",
                QueryKeyPart.Text("42"),
                QueryKeyPart.Signed(7),
                QueryKeyPart.Boolean(true));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void PartKindsParticipateInEquality()
        {
            QueryKey<string> signed = QueryKey.For<string>(
                "value",
                QueryKeyPart.Signed(1));
            QueryKey<string> unsigned = QueryKey.For<string>(
                "value",
                QueryKeyPart.Unsigned(1));

            Assert.That(signed, Is.Not.EqualTo(unsigned));
        }

        [Test]
        public void EmptyScopeIsRejected()
        {
            Assert.Throws<ArgumentException>(() => QueryKey.For<string>(" "));
        }

        [Test]
        public void DefaultKeyPartIsRejected()
        {
            Assert.Throws<ArgumentException>(
                () => QueryKey.For<string>("player", default(QueryKeyPart)));
        }

        [Test]
        public void ReusingStructuralIdentityWithAnotherResultTypeThrows()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> textKey = QueryKey.For<string>("shared");
            QueryKey<int> numberKey = QueryKey.For<int>("shared");

            client.SetData(textKey, "value");

            Assert.Throws<QueryTypeMismatchException>(() => client.SetData(numberKey, 1));
        }

        [Test]
        public void InvalidPolicyDurationsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new QueryPolicy(staleAfter: TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new QueryPolicy(unusedFor: TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new QueryPolicy(pollEvery: TimeSpan.Zero));
        }
    }
}
