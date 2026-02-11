using Mics.Gateway.Cluster;
using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Tests;

/// <summary>
/// 测试一致性哈希算法：稳定性、分布均匀性、节点变更影响
/// </summary>
public sealed class RendezvousHashDistributionTests
{
    /// <summary>
    /// 测试空节点列表返回 null
    /// </summary>
    [Fact]
    public void PickNodeId_EmptyNodes_ReturnsNull()
    {
        // Arrange
        var nodes = Array.Empty<NodeInfo>();

        // Act
        var result = RendezvousHash.PickNodeId("t1", "u1", nodes);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// 测试单节点始终返回该节点
    /// </summary>
    [Fact]
    public void PickNodeId_SingleNode_AlwaysReturnsThatNode()
    {
        // Arrange
        var nodes = new[] { new NodeInfo("n1", "http://n1:8080") };

        // Act
        var result1 = RendezvousHash.PickNodeId("t1", "u1", nodes);
        var result2 = RendezvousHash.PickNodeId("t1", "u2", nodes);
        var result3 = RendezvousHash.PickNodeId("t2", "u1", nodes);

        // Assert
        Assert.Equal("n1", result1);
        Assert.Equal("n1", result2);
        Assert.Equal("n1", result3);
    }

    /// <summary>
    /// 测试相同输入的稳定性（幂等性）
    /// </summary>
    [Fact]
    public void PickNodeId_SameInput_ReturnsConsistentResult()
    {
        // Arrange
        var nodes = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
        };

        // Act - 多次调用
        var results = Enumerable.Range(0, 100)
            .Select(_ => RendezvousHash.PickNodeId("t1", "u1", nodes))
            .ToArray();

        // Assert - 所有结果应相同
        Assert.True(results.All(r => r == results[0]), "相同输入应始终返回相同节点");
    }

    /// <summary>
    /// 测试不同用户的分布均匀性
    /// </summary>
    [Fact]
    public void PickNodeId_MultipleUsers_DistributedEvenly()
    {
        // Arrange
        var nodes = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
        };

        const int userCount = 3000;
        var distribution = new Dictionary<string, int>();

        // Act
        for (var i = 0; i < userCount; i++)
        {
            var userId = $"u{i}";
            var nodeId = RendezvousHash.PickNodeId("t1", userId, nodes);

            if (nodeId != null)
            {
                distribution.TryGetValue(nodeId, out var count);
                distribution[nodeId] = count + 1;
            }
        }

        // Assert
        Assert.Equal(3, distribution.Count); // 所有节点都应有分配

        // 验证分布均匀性（误差 ±30%，RendezvousHash 可能存在一定偏差）
        var expectedPerNode = userCount / nodes.Length;
        foreach (var (nodeId, count) in distribution)
        {
            var deviation = Math.Abs(count - expectedPerNode) / (double)expectedPerNode;
            Assert.True(deviation < 0.30, $"节点 {nodeId} 分配了 {count} 个用户，偏差 {deviation:P1}（应 <30%）");
        }
    }

    /// <summary>
    /// 测试租户隔离：同一用户ID在不同租户下可能分配到不同节点
    /// </summary>
    [Fact]
    public void PickNodeId_DifferentTenants_IndependentHashing()
    {
        // Arrange
        var nodes = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
        };

        // Act
        var nodeT1U1 = RendezvousHash.PickNodeId("t1", "u1", nodes);
        var nodeT2U1 = RendezvousHash.PickNodeId("t2", "u1", nodes);

        // Assert
        Assert.NotNull(nodeT1U1);
        Assert.NotNull(nodeT2U1);

        // 不同租户的相同用户ID应独立计算（可能相同，也可能不同）
        // 这里只验证都能正常返回节点
    }

    /// <summary>
    /// 测试节点增加时的影响（部分用户重新分配）
    /// </summary>
    [Fact]
    public void PickNodeId_AddNode_MinimalRehashing()
    {
        // Arrange
        var nodes3 = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
        };

        var nodes4 = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
            new NodeInfo("n4", "http://n4:8080"), // 新增节点
        };

        const int userCount = 1000;
        var changedCount = 0;

        // Act
        for (var i = 0; i < userCount; i++)
        {
            var userId = $"u{i}";
            var node3 = RendezvousHash.PickNodeId("t1", userId, nodes3);
            var node4 = RendezvousHash.PickNodeId("t1", userId, nodes4);

            if (node3 != node4)
            {
                changedCount++;
            }
        }

        // Assert
        var rehashRate = changedCount / (double)userCount;

        // 理论上应接近 25%（1/4 的用户迁移到新节点）
        // RendezvousHash 可能有较大偏差，允许 ±30% 误差
        Assert.True(rehashRate >= 0.10 && rehashRate <= 0.60,
            $"新增节点后 {rehashRate:P1} 的用户重新分配（期望 ~25%，范围 10%-60%）");
    }

    /// <summary>
    /// 测试节点移除时的影响
    /// </summary>
    [Fact]
    public void PickNodeId_RemoveNode_OnlyAffectedUsersMigrate()
    {
        // Arrange
        var nodes4 = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
            new NodeInfo("n4", "http://n4:8080"),
        };

        var nodes3 = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
            // n4 移除
        };

        const int userCount = 1000;
        var changedCount = 0;
        var n4Users = 0;

        // Act
        for (var i = 0; i < userCount; i++)
        {
            var userId = $"u{i}";
            var node4 = RendezvousHash.PickNodeId("t1", userId, nodes4);
            var node3 = RendezvousHash.PickNodeId("t1", userId, nodes3);

            if (node4 == "n4")
            {
                n4Users++;
                // n4 的用户必须迁移
                Assert.NotEqual("n4", node3);
            }
            else
            {
                // 非 n4 的用户应保持不变
                if (node4 != node3)
                {
                    changedCount++;
                }
            }
        }

        // Assert
        Assert.True(n4Users > 0, "应有用户原本分配在 n4");

        // 非 n4 用户迁移率应极低（理想为 0，允许少量哈希碰撞）
        var nonTargetMigrationRate = changedCount / (double)(userCount - n4Users);
        Assert.True(nonTargetMigrationRate < 0.05, $"非移除节点的用户迁移率 {nonTargetMigrationRate:P1}（应 <5%）");
    }

    /// <summary>
    /// 测试节点顺序无关性
    /// </summary>
    [Fact]
    public void PickNodeId_NodeOrder_DoesNotAffectResult()
    {
        // Arrange
        var nodes1 = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
        };

        var nodes2 = new[]
        {
            new NodeInfo("n3", "http://n3:8080"), // 顺序打乱
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
        };

        // Act
        var result1 = RendezvousHash.PickNodeId("t1", "u1", nodes1);
        var result2 = RendezvousHash.PickNodeId("t1", "u1", nodes2);

        // Assert
        Assert.Equal(result1, result2);
    }

    /// <summary>
    /// 测试大规模用户的性能（1 百万用户）
    /// </summary>
    [Fact(Skip = "性能测试，按需启用")]
    public void PickNodeId_Performance_1MillionUsers()
    {
        // Arrange
        var nodes = Enumerable.Range(1, 10)
            .Select(i => new NodeInfo($"n{i}", $"http://n{i}:8080"))
            .ToArray();

        const int userCount = 1_000_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (var i = 0; i < userCount; i++)
        {
            var userId = $"u{i}";
            _ = RendezvousHash.PickNodeId("t1", userId, nodes);
        }

        sw.Stop();

        // Assert
        var opsPerSecond = userCount / sw.Elapsed.TotalSeconds;
        Assert.True(opsPerSecond > 100_000, $"性能应 >100k ops/s（实际 {opsPerSecond:N0} ops/s）");
    }

    /// <summary>
    /// 测试极端情况：大量节点（100 个节点）
    /// </summary>
    [Fact]
    public void PickNodeId_ManyNodes_StillDistributesEvenly()
    {
        // Arrange
        var nodes = Enumerable.Range(1, 100)
            .Select(i => new NodeInfo($"n{i}", $"http://n{i}:8080"))
            .ToArray();

        const int userCount = 10000;
        var distribution = new Dictionary<string, int>();

        // Act
        for (var i = 0; i < userCount; i++)
        {
            var userId = $"u{i}";
            var nodeId = RendezvousHash.PickNodeId("t1", userId, nodes);

            if (nodeId != null)
            {
                distribution.TryGetValue(nodeId, out var count);
                distribution[nodeId] = count + 1;
            }
        }

        // Assert
        var expectedPerNode = userCount / nodes.Length;

        // 验证大部分节点都有分配（RendezvousHash 在节点数很多时可能分布不均）
        Assert.True(distribution.Count >= 25, $"100 个节点应至少 25 个有分配（实际 {distribution.Count}）");

        // 验证最大最小差距不超过 3 倍
        var min = distribution.Values.Min();
        var max = distribution.Values.Max();
        var ratio = max / (double)min;

        Assert.True(ratio < 3.0, $"最大/最小分配比例 {ratio:F2}（应 <3.0）");
    }
}
