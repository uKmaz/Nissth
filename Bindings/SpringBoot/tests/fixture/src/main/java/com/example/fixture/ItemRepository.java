package com.example.fixture;

import java.util.List;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

/**
 * Spring Data JPA repository for {@link Item}.
 *
 * <p>Per {@code CLAUDE.md} §8.5 forbidden pattern #9, the binding's fixture
 * uses {@code JpaRepository} as the default persistence access pattern rather
 * than raw {@code JdbcTemplate}. Derived query methods (e.g.,
 * {@link #findByNameContainingIgnoreCase(String)}) demonstrate how the
 * fixture surfaces queryable shapes without writing JPQL.
 */
@Repository
public interface ItemRepository extends JpaRepository<Item, Long> {

    List<Item> findByNameContainingIgnoreCase(String fragment);
}
